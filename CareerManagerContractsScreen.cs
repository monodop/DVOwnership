using DV.CashRegister;
using DV.Logic.Job;
using DV.Printers;
using DV.ServicePenalty;
using DV.ServicePenalty.UI;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TMPro;
using UnityEngine;

namespace DVOwnership
{
    public class CareerManagerContractsScreen : MonoBehaviour, IDisplayScreen
    {

        public DisplayScreenSwitcher screenSwitcher;

        public CareerManagerMainScreen mainScreen;

        public CashRegisterCareerManager cashReg;

        //public PrinterController feePrinter;

        public TextMeshPro title1;

        public TextMeshPro title2;

        public TextMeshPro desc1;

        public TextMeshPro desc2;

        public TextMeshPro insertWallet;

        public TextMeshPro depositedText;

        public TextMeshPro depositedValue;

        public DisplayableDebt DebtToPay
        {
            get;
            private set;
        }

        private void Awake()
        {
            if (screenSwitcher == null)
            {
                Debug.LogError("screenSwitcher reference isn't set! Screen can't function!");
            }
            else if (cashReg == null)
            {
                Debug.LogError("cashReg reference isn't set! Screen can't function!");
            }
            //else if (licensePrinter == null)
            //{
            //    Debug.LogError("licensePrinter reference isn't set! Screen can't function!");
            //}
        }

        public void Activate(IDisplayScreen previousScreen)
        {
            float num = 1000;
            cashReg.SetTotalCost(num);
            cashReg.CashAdded += OnCashAdded;
            title1.text = "Regenerate Contracts";
            title2.text = "Remove all unaccepted contracts";
            desc1.text = "and generate new ones";
            desc2.text = "$" + num.ToString("F2");
            insertWallet.text = "Insert wallet to pay.";
            depositedText.text = "DEPOSITED: ";
            depositedValue.text = "$" + cashReg.DepositedCash.ToString("F2");
        }

        public void Disable()
        {
            cashReg.ClearCurrentTransaction();
            cashReg.CashAdded -= OnCashAdded;

            title1.text = string.Empty;
            title2.text = string.Empty;
            desc1.text = string.Empty;
            desc2.text = string.Empty;
            insertWallet.text = string.Empty;
            depositedText.text = string.Empty;
            depositedValue.text = string.Empty;
        }

        public void HandleInputAction(InputAction input)
        {
            //Vector3 position = licensePrinter.spawnAnchor.position;
            //Quaternion rotation = licensePrinter.spawnAnchor.rotation;
            Transform parent = SingletonBehaviour<WorldMover>.Exists ? SingletonBehaviour<WorldMover>.Instance.originShiftParent : null;
            switch (input)
            {
                case InputAction.Cancel:
                    screenSwitcher.SetActiveDisplay(mainScreen);
                    break;
                case InputAction.Confirm:
                    if (!cashReg.Buy())
                    {
                        break;
                    }

                    // Do action
                    DVOwnership.LogDebug(() => "Completed Purchase");
                    var stations = StationController.allStations;
                    var playerEnteredJobGenerationZoneField = typeof(StationController)
                        .GetField("playerEnteredJobGenerationZone", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
                    foreach (var station in stations)
                    {
                        var playerEnteredJobGenerationZone = (bool)playerEnteredJobGenerationZoneField.GetValue(station);
                        if (playerEnteredJobGenerationZone)
                        {
                            station.ExpireAllAvailableJobsInStation();
                            station.ProceduralJobsController.TryToGenerateJobs();
                        }
                    }

                    //licensePrinter.Print(ignoreCooldown: true);
                    screenSwitcher.SetActiveDisplay(mainScreen);
                    break;
                case InputAction.PrintInfo:
                    //if (licensePrinter.IsOnCooldown)
                    //{
                    //    licensePrinter.PlayErrorSound();
                    //    break;
                    //}

                    DVOwnership.LogDebug(() => "Print");
                    //if (IsJobLicense)
                    //{
                    //    BookletCreator.CreateLicenseInfo(jobLicenseToBuy, position, rotation, parent);
                    //}
                    //else
                    //{
                    //    if (!IsGeneralLicense)
                    //    {
                    //        Debug.LogError("InvalidState: license to buy is not set!");
                    //        break;
                    //    }

                    //    BookletCreator.CreateLicenseInfo(generalLicenseToBuy, position, rotation, parent);
                    //}

                    //licensePrinter.Print();
                    break;
            }
        }

        private void OnCashAdded()
        {
            depositedValue.text = "$" + cashReg.DepositedCash.ToString("F2");
        }
    }
}
