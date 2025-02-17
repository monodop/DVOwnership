﻿using DV;
using DV.PointSet;
using DVOwnership.Patches;
using Harmony12;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace DVOwnership
{
    public class CommsRadioEquipmentPurchaser : MonoBehaviour, ICommsRadioMode
    {
        public static CommsRadioController controller;

        private static HashSet<TrainCarType> bannedTypes = new HashSet<TrainCarType>
        {
            TrainCarType.NotSet,
            // Crew vehicle types are added by the Awake method
        };
        private static Dictionary<TrainCarType, TrainCarType> locomotiveForTender = new Dictionary<TrainCarType, TrainCarType>
        {
            { TrainCarType.Tender, TrainCarType.LocoSteamHeavy },
            { TrainCarType.TenderBlue, TrainCarType.LocoSteamHeavyBlue },
        };

        public ButtonBehaviourType ButtonBehaviour { get; private set; }

        private const float POTENTIAL_TRACKS_RADIUS = 200f;
        private const float MAX_DISTANCE_FROM_TRACK_POINT = 3f;
        private const float TRACK_POINT_POSITION_Y_OFFSET = -1.75f;
        private const float SIGNAL_RANGE = 100f;
        private const float INVALID_DESTINATION_HIGHLIGHTER_DISTANCE = 20f;
        private const float UPDATE_TRACKS_PERIOD = 2.5f;

        private static Color laserColor = new Color(1f, 0f, 0.9f, 1f);
        public Color GetLaserBeamColor() { return laserColor; }

        [Header("Strings")]
        private const string MODE_NAME = "ROLLING STOCK";
        private const string CONTENT_MAINMENU = "Buy equipment?";
        private const string CONTENT_SELECT_CAR = "{0}\n${1}\n\n{2}";
        private const string CONTENT_SELECT_DESTINATION = "{0}\n{1}m\n\n{2}";
        private const string CONTENT_CONFIRM_PURCHASE = "Buy {0} for ${1}?\n\n{2}";
        private const string CONTENT_FRAGMENT_INSUFFICIENT_FUNDS = "Insufficient funds.";
        private const string ACTION_CONFIRM_SELECTION = "buy";
        private const string ACTION_CONFIRM_DESTINATION = "place";
        private const string ACTION_CONFIRM_PURCHASE = "confirm";
        private const string ACTION_CANCEL = "cancel";

        public Transform signalOrigin;
        public void OverrideSignalOrigin(Transform signalOrigin) { this.signalOrigin = signalOrigin; }

        public CommsRadioDisplay display;
        public Material validMaterial;
        public Material invalidMaterial;
        public ArrowLCD lcdArrow;

        [Header("Sounds")]
        public AudioClip spawnModeEnterSound;
        public AudioClip spawnVehicleSound;
        public AudioClip confirmSound;
        public AudioClip cancelSound;
        public AudioClip hoverOverCar;
        public AudioClip warningSound;
        public AudioClip moneyRemovedSound;

        [Header("Highlighters")]
        public GameObject destinationHighlighterGO;
        public GameObject directionArrowsHighlighterGO;
        private CarDestinationHighlighter destinationHighlighter;
        private RaycastHit hit;
        private LayerMask trackMask;
        private LayerMask laserPointerMask;

        private List<TrainCarType> carTypesAvailableForPurchase;
        private int selectedCarTypeIndex = 0;
        private GameObject carPrefabToSpawn;
        private Bounds carBounds;
        private float carLength;
        private float carPrice;

        private bool spawnWithTrackDirection = true;
        private List<RailTrack> potentialTracks = new List<RailTrack>();
        private bool canSpawnAtPoint;
        private RailTrack destinationTrack;
        private EquiPointSet.Point? closestPointOnDestinationTrack;
        private Coroutine trackUpdateCoro;

        private bool isPurchaseConfirmed = true;

        private State state;
        protected enum State
        {
            NotActive,
            MainMenu,
            PickCar,
            PickDestination,
            ConfirmPurchase,
        }
        protected enum Action
        {
            Trigger,
            Increase,
            Decrease,
        }

        #region Unity Lifecycle

        public void Awake()
        {
            try
            {
                // Copy components from other radio modes
                var summoner = controller.crewVehicleControl;

                if (summoner == null) { throw new Exception("Crew vehicle radio mode could not be found!"); }

                signalOrigin = summoner.signalOrigin;
                display = summoner.display;
                validMaterial = summoner.validMaterial;
                invalidMaterial = summoner.invalidMaterial;
                lcdArrow = summoner.lcdArrow;
                destinationHighlighterGO = summoner.destinationHighlighterGO;
                directionArrowsHighlighterGO = summoner.directionArrowsHighlighterGO;

                spawnModeEnterSound = summoner.spawnModeEnterSound;
                spawnVehicleSound = summoner.spawnVehicleSound;
                confirmSound = summoner.confirmSound;
                cancelSound = summoner.cancelSound;
                hoverOverCar = summoner.hoverOverCar;
                warningSound = summoner.warningSound;
                moneyRemovedSound = summoner.moneyRemovedSound;
            }
            catch (Exception e) { DVOwnership.OnCriticalFailure(e, "copying radio components"); }

            try
            {
                // Crew vehicles use the vanilla crew vehicle summoning logic, so they can't be purchased.
                var summoner = controller.crewVehicleControl;
                var garageCarSpawners = AccessTools.Field(typeof(CommsRadioCrewVehicle), "garageCarSpawners").GetValue(summoner) as GarageCarSpawner[];
                if (garageCarSpawners != null)
                {
                    foreach (var garageSpawner in garageCarSpawners)
                    {
                        bannedTypes.Add(garageSpawner.locoType);
                    }
                }
            }
            catch (Exception e) { DVOwnership.OnCriticalFailure(e, "banning crew vehicles from purchase"); }

            if (!signalOrigin)
            {
                DVOwnership.LogWarning("signalOrigin on CommsRadioEquipmentPurchaser is missing. Using this.transform instead.");
                signalOrigin = transform;
            }

            if (display == null)
            {
                DVOwnership.LogError("display is missing. Can't function properly!");
            }

            if (validMaterial == null || invalidMaterial == null)
            {
                DVOwnership.LogWarning("Some of the required materials are missing. Visuals won't be correct!");
            }

            if (destinationHighlighterGO == null)
            {
                DVOwnership.LogError("destinationHighlighterGO is missing. Can't function properly!");
            }

            if (directionArrowsHighlighterGO == null)
            {
                DVOwnership.LogError("directionArrowsHighlighterGO is missing. Can't function properly!");
            }

            if (spawnModeEnterSound == null || spawnVehicleSound == null || confirmSound == null || cancelSound == null || hoverOverCar == null || warningSound == null || moneyRemovedSound == null)
            {
                DVOwnership.LogWarning("Some audio clips are missing. Some sounds won't be played!");
            }

            trackMask = LayerMask.GetMask(new string[] { "Default" });
            laserPointerMask = LayerMask.GetMask(new string[] { "Laser_Pointer_Target" });

            destinationHighlighter = new CarDestinationHighlighter(destinationHighlighterGO, directionArrowsHighlighterGO);

            LicenseManager.JobLicenseAcquired += OnLicenseAcquired;
        }

        public void Start()
        {
            // TODO: does anything go in here?
        }

        private void OnDestroy()
        {
            if (UnloadWatcher.isUnloading) { return; }

            destinationHighlighter.Destroy();
            destinationHighlighter = null;
        }

        #endregion

        #region ICommsRadioMode

        public void Enable() { TransitionToState(State.MainMenu); }

        public void Disable() { TransitionToState(State.NotActive); }

        public void SetStartingDisplay() { display.SetDisplay(MODE_NAME, CONTENT_MAINMENU); }

        public void OnUpdate()
        {
            bool isDisplayUpdateNeeded = false;
            bool hasAffordabilityChanged = HasAffordabilityChanged;

            switch (state)
            {
                case State.NotActive:
                case State.MainMenu:
                    break;

                case State.PickDestination:
                    if (potentialTracks.Count > 0 && Physics.Raycast(signalOrigin.position, signalOrigin.forward, out hit, SIGNAL_RANGE, trackMask))
                    {
                        var point = hit.point;
                        foreach(var railTrack in potentialTracks)
                        {
                            var pointWithinRangeWithYOffset = RailTrack.GetPointWithinRangeWithYOffset(railTrack, point, MAX_DISTANCE_FROM_TRACK_POINT, TRACK_POINT_POSITION_Y_OFFSET);
                            if (pointWithinRangeWithYOffset.HasValue)
                            {
                                destinationTrack = railTrack;
                                var trackPoints = railTrack.GetPointSet(0f).points;
                                var index = pointWithinRangeWithYOffset.Value.index;
                                var closestSpawnablePoint = CarSpawner.FindClosestValidPointForCarStartingFromIndex(trackPoints, index, carBounds.extents);
                                var flag = closestSpawnablePoint != null;
                                if (canSpawnAtPoint != flag) { isDisplayUpdateNeeded = true; }
                                canSpawnAtPoint = flag;
                                if (canSpawnAtPoint) { closestPointOnDestinationTrack = closestSpawnablePoint; }
                                else { closestPointOnDestinationTrack = pointWithinRangeWithYOffset; }
                                HighlightClosestPointOnDestinationTrack();
                                goto default;
                            }
                        }
                    }
                    if (canSpawnAtPoint) { isDisplayUpdateNeeded = true; }
                    canSpawnAtPoint = false;
                    destinationTrack = null;
                    HighlightInvalidPoint();
                    goto default;

                case State.ConfirmPurchase:
                    if (hasAffordabilityChanged && destinationTrack != null) { HighlightClosestPointOnDestinationTrack(); }
                    goto default;
                
                default:
                    if (hasAffordabilityChanged) { isDisplayUpdateNeeded = true; }
                    break;
            }

            if (isDisplayUpdateNeeded) { TransitionToState(state); }
        }

        public void OnUse()
        {
            TransitionToState(DispatchAction(Action.Trigger));
        }

        public bool ButtonACustomAction()
        {
            TransitionToState(DispatchAction(Action.Decrease));
            return true;
        }

        public bool ButtonBCustomAction()
        {
            TransitionToState(DispatchAction(Action.Increase));
            return true;
        }

        #endregion

        private void ClearFlags()
        {
            destinationTrack = null;
            canSpawnAtPoint = false;
            destinationHighlighter.TurnOff();
        }

        #region State Machine

        private State DispatchAction(Action action)
        {
            switch (state)
            {
                case State.MainMenu:
                    switch (action)
                    {
                        case Action.Trigger:
                            return State.PickCar;
                        default:
                            DVOwnership.LogError($"Unexpected state/action pair! state: {state}, action: {action}");
                            return State.MainMenu;
                    }

                case State.PickCar:
                    switch (action)
                    {
                        case Action.Trigger:
                            return CanAfford ? State.PickDestination : State.MainMenu;
                        case Action.Increase:
                            SelectNextCar();
                            break;
                        case Action.Decrease:
                            SelectPrevCar();
                            break;
                    }
                    return State.PickCar;

                case State.PickDestination:
                    switch (action)
                    {
                        case Action.Trigger:
                            return CanAfford && canSpawnAtPoint ? State.ConfirmPurchase : State.MainMenu;
                        case Action.Increase:
                        case Action.Decrease:
                            ReverseSpawnDirection();
                            break;
                    }
                    return State.PickDestination;

                case State.ConfirmPurchase:
                    switch (action)
                    {
                        case Action.Trigger:
                            return State.MainMenu;
                        case Action.Increase:
                        case Action.Decrease:
                            ToggleConfirmation();
                            break;
                    }
                    return State.ConfirmPurchase;
            }

            DVOwnership.LogError($"Reached end of DispatchAction without returning a new state. This should never happen! state: {state}, action: {action}");
            return state;
        }

        private void TransitionToState(State newState)
        {
            var oldState = state;
            state = newState;

            switch (newState)
            {
                case State.NotActive:
                    ButtonBehaviour = ButtonBehaviourType.Regular;
                    ClearFlags();
                    trackUpdateCoro = null;
                    StopAllCoroutines();
                    return;

                case State.MainMenu:
                    if (oldState == State.ConfirmPurchase && isPurchaseConfirmed && CanAfford)
                    {
                        // Completed purchase
                        CommsRadioController.PlayAudioFromRadio(confirmSound, transform);
                        DeductFunds(carPrice);
                        SpawnCar();
                    }
                    else if (oldState != State.NotActive)
                    {
                        // Canceled purchase
                        CommsRadioController.PlayAudioFromRadio(cancelSound, transform);
                    }
                    ButtonBehaviour = ButtonBehaviourType.Regular;
                    DisplayMainMenu();
                    ClearFlags();
                    return;

                case State.PickCar:
                    if (oldState == State.MainMenu)
                    {
                        CommsRadioController.PlayAudioFromRadio(spawnModeEnterSound, transform);
                        UpdateCarTypesAvailableForPurchase();
                    }
                    ButtonBehaviour = ButtonBehaviourType.Override;
                    UpdateCarToSpawn();
                    DisplayCarTypeAndPrice();
                    return;

                case State.PickDestination:
                    if (oldState == State.PickCar)
                    {
                        CommsRadioController.PlayAudioFromRadio(confirmSound, transform);
                        if (trackUpdateCoro == null) { trackUpdateCoro = StartCoroutine(PotentialTracksUpdateCoro()); }
                    }
                    ButtonBehaviour = ButtonBehaviourType.Override;
                    DisplayCarTypeAndLength();
                    return;

                case State.ConfirmPurchase:
                    if (oldState == State.PickDestination)
                    {
                        CommsRadioController.PlayAudioFromRadio(confirmSound, transform);
                        StopAllCoroutines();
                        isPurchaseConfirmed = true;
                    }
                    ButtonBehaviour = ButtonBehaviourType.Override;
                    DisplayPurchaseConfirmation();
                    return;
            }

            DVOwnership.LogError($"Reached end of TransitionToState while transitioning from {oldState} to {newState}. This should never happen!");
        }

        #endregion

        #region LCD Display

        private void DisplayMainMenu()
        {
            SetStartingDisplay();
            lcdArrow.TurnOff();
        }

        private void DisplayCarTypeAndPrice()
        {
            var content = string.Format(CONTENT_SELECT_CAR, SelectedCarType.DisplayName(), carPrice.ToString("F0"), CanAfford ? "" : CONTENT_FRAGMENT_INSUFFICIENT_FUNDS);
            var action = CanAfford ? ACTION_CONFIRM_SELECTION : ACTION_CANCEL;
            display.SetContentAndAction(content, action);
            lcdArrow.TurnOff();
        }

        private void DisplayCarTypeAndLength()
        {
            var content = string.Format(CONTENT_SELECT_DESTINATION, SelectedCarType.DisplayName(), carLength.ToString("F"), CanAfford ? "" : CONTENT_FRAGMENT_INSUFFICIENT_FUNDS);
            var action = CanAfford && canSpawnAtPoint ? ACTION_CONFIRM_DESTINATION : ACTION_CANCEL;
            display.SetContentAndAction(content, action);
            if (canSpawnAtPoint) { UpdateLCDRerailDirectionArrow(); }
            else { lcdArrow.TurnOff(); }
        }

        private void DisplayPurchaseConfirmation()
        {
            var content = string.Format(CONTENT_CONFIRM_PURCHASE, SelectedCarType.DisplayName(), carPrice.ToString("F0"), CanAfford ? "" : CONTENT_FRAGMENT_INSUFFICIENT_FUNDS);
            var action = CanAfford && isPurchaseConfirmed ? ACTION_CONFIRM_PURCHASE : ACTION_CANCEL;
            display.SetContentAndAction(content , action);
            lcdArrow.TurnOff();
        }

        private void UpdateLCDRerailDirectionArrow()
        {
            bool flag = Mathf.Sin(Vector3.SignedAngle(spawnWithTrackDirection ? closestPointOnDestinationTrack.Value.forward : (-closestPointOnDestinationTrack.Value.forward), signalOrigin.forward, Vector3.up) * 0.0174532924f) <= 0f;
            lcdArrow.TurnOn(!flag);
        }

        #endregion

        #region Finances

        private bool CanAfford
        {
            get { return SingletonBehaviour<Inventory>.Instance.PlayerMoney >= carPrice; }
        }

        private bool _couldAfford;
        private bool HasAffordabilityChanged
        {
            get
            {
                var canAfford = CanAfford;
                var couldAfford = _couldAfford;
                _couldAfford = canAfford;
                return canAfford != couldAfford;
            }
        }

        private float CalculateCarPrice(TrainCarType carType)
        {
            var isLoco = CarTypes.IsLocomotive(carType);
            var price = ResourceTypes.GetFullDamagePriceForCar(carType);
            if (isLoco) { price = ScaleLocoPrice(price); }
            if (DVOwnership.Settings.isPriceScaledWithDifficulty) { price = ScalePriceBasedOnDifficulty(price, isLoco); }
            return Mathf.Round(price);
        }

        private float ScaleLocoPrice(float price)
        {
            return price * 10f;
        }

        private float ScalePriceBasedOnDifficulty(float price, bool isLoco)
        {
            switch (GamePreferences.Get<CareerDifficultyValues>(Preferences.CareerDifficulty))
            {
                case CareerDifficultyValues.HARDCORE:
                    return Mathf.Pow(price / 10_000f, 1.1f) * 10_000f;
                case CareerDifficultyValues.CASUAL:
                    return price / (isLoco ? 100f : 10f);
                default:
                    return price;
            }
        }

        private void DeductFunds(float price)
        {
            SingletonBehaviour<Inventory>.Instance.RemoveMoney(price);
            if (moneyRemovedSound != null) { moneyRemovedSound.Play2D(1f, false); }
        }

        #endregion

        #region TrainCar Selection

        private void SelectNextCar()
        {
            selectedCarTypeIndex++;
            if (selectedCarTypeIndex >= carTypesAvailableForPurchase.Count) { selectedCarTypeIndex = 0; }
        }

        private void SelectPrevCar()
        {
            selectedCarTypeIndex--;
            if (selectedCarTypeIndex < 0) { selectedCarTypeIndex = carTypesAvailableForPurchase.Count - 1; }
        }

        private TrainCarType SelectedCarType { get { return carTypesAvailableForPurchase[selectedCarTypeIndex]; } }

        private void UpdateCarToSpawn()
        {
            var carType = SelectedCarType;

            carPrice = CalculateCarPrice(carType);

            carPrefabToSpawn = CarTypes.GetCarPrefab(carType);
            if (carPrefabToSpawn == null)
            {
                carPrice = float.PositiveInfinity;
                DVOwnership.LogError($"Couldn't load car prefab: {carType}! Won't be able to spawn this car.");
                return;
            }

            var trainCar = carPrefabToSpawn.GetComponent<TrainCar>();
            carBounds = trainCar.Bounds;
            carLength = trainCar.InterCouplerDistance;
        }

        public void UpdateCarTypesAvailableForPurchase()
        {
            var prevSelectedCarType = carTypesAvailableForPurchase?.Count > 0 ? SelectedCarType : TrainCarType.NotSet;
            var allowedCarTypes = from carType in Enum.GetValues(typeof(TrainCarType)).Cast<TrainCarType>()
                              where !bannedTypes.Contains(carType) && !CarTypes.IsHidden(carType)
                              select carType;
            var licensedCarTypes = from carType in allowedCarTypes
                                   where CarTypes.IsAnyLocomotiveOrTender(carType) ? LicenseManager_Patches.IsLicensedForLoco(LocoForTender(carType)) : LicenseManager_Patches.IsLicensedForCar(carType)
                                   select carType;
            carTypesAvailableForPurchase = licensedCarTypes.ToList();
            selectedCarTypeIndex = carTypesAvailableForPurchase.FindIndex(carType => carType == prevSelectedCarType);
            if (selectedCarTypeIndex == -1) { selectedCarTypeIndex = 0; }
        }

        private TrainCarType LocoForTender(TrainCarType carType)
        {
            return locomotiveForTender.ContainsKey(carType) ? locomotiveForTender[carType] : carType;
        }

        private void OnLicenseAcquired(JobLicenses jobLicenses)
        {
            if (state == State.PickCar) { UpdateCarTypesAvailableForPurchase(); }
        }

        #endregion

        #region Track Selection

        private void UpdatePotentialTracks()
        {
            potentialTracks.Clear();
            for (float radius = POTENTIAL_TRACKS_RADIUS; potentialTracks.Count == 0 && radius <= 800f; radius += 40f)
            {
                if (radius > POTENTIAL_TRACKS_RADIUS) { DVOwnership.LogWarning($"No tracks in {radius} radius. Expanding radius."); }
                foreach (var railTrack in RailTrackRegistry.AllTracks)
                {
                    if (RailTrack.GetPointWithinRangeWithYOffset(railTrack, transform.position, radius, 0f) != null)
                    {
                        potentialTracks.Add(railTrack);
                    }
                }
            }
            if (potentialTracks.Count == 0) { DVOwnership.LogError("No nearby tracks found. Can't spawn rolling stock!"); }
        }

        private IEnumerator PotentialTracksUpdateCoro()
        {
            Vector3 lastUpdatedTracksWorldPosition = Vector3.positiveInfinity;
            while (true)
            {
                if ((transform.position - WorldMover.currentMove - lastUpdatedTracksWorldPosition).magnitude > 100f)
                {
                    UpdatePotentialTracks();
                    lastUpdatedTracksWorldPosition = transform.position - WorldMover.currentMove;
                }
                yield return WaitFor.Seconds(UPDATE_TRACKS_PERIOD);
            }
        }

        private void HighlightClosestPointOnDestinationTrack()
        {
            var position = (Vector3)closestPointOnDestinationTrack.Value.position + WorldMover.currentMove;
            var vector = closestPointOnDestinationTrack.Value.forward;
            if (!spawnWithTrackDirection) { vector *= -1f; }

            destinationHighlighter.Highlight(position, vector, carBounds, CanAfford && canSpawnAtPoint ? validMaterial : invalidMaterial);
        }

        private void HighlightInvalidPoint()
        {
            destinationHighlighter.Highlight(signalOrigin.position + signalOrigin.forward * INVALID_DESTINATION_HIGHLIGHTER_DISTANCE, signalOrigin.right, carBounds, invalidMaterial);
        }

        #endregion

        #region Car Spawning

        private void SpawnCar()
        {
            if (!canSpawnAtPoint) { return; }

            var position = (Vector3)closestPointOnDestinationTrack.Value.position + WorldMover.currentMove;
            var vector = closestPointOnDestinationTrack.Value.forward;
            vector = spawnWithTrackDirection ? vector : -vector;

            var trainCar = CarSpawner.SpawnCar(carPrefabToSpawn, destinationTrack, position, vector);
            if (trainCar == null)
            {
                DVOwnership.LogError($"Couldn't spawn {SelectedCarType}!");
                return;
            }

            CommsRadioController.PlayAudioFromCar(spawnVehicleSound, trainCar);
            SingletonBehaviour<RollingStockManager>.Instance.Add(Equipment.FromTrainCar(trainCar));
            SingletonBehaviour<UnusedTrainCarDeleter>.Instance.MarkForDelete(trainCar);
        }

        private void ReverseSpawnDirection()
        {
            spawnWithTrackDirection = !spawnWithTrackDirection;
        }

        private void ToggleConfirmation()
        {
            isPurchaseConfirmed = !isPurchaseConfirmed;
        }

        #endregion

        [HarmonyPatch(typeof(CommsRadioController), "Awake")]
        class CommsRadioController_Awake_Patch
        {
            public static CommsRadioEquipmentPurchaser equipmentPurchaser = null;

            static void Postfix(CommsRadioController __instance, List<ICommsRadioMode> ___allModes)
            {
                controller = __instance;

                if (equipmentPurchaser == null) { equipmentPurchaser = controller.gameObject.AddComponent<CommsRadioEquipmentPurchaser>(); }

                if (!___allModes.Contains(equipmentPurchaser))
                {
                    int spawnerIndex = ___allModes.FindIndex(mode => mode is CommsRadioCarSpawner);
                    if (spawnerIndex != -1) { ___allModes.Insert(spawnerIndex, equipmentPurchaser); }
                    else { ___allModes.Add(equipmentPurchaser); }
                    controller.ReactivateModes();
                }
            }
        }
    }
}
