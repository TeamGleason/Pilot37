using System;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Diagnostics;
using System.Diagnostics.Tracing;
using System.Runtime.InteropServices.WindowsRuntime;
using Windows.Foundation;
using Windows.Foundation.Collections;
using Windows.UI;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Controls.Primitives;
using Windows.UI.Core;
using Windows.UI.Xaml.Input;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Navigation;
using Windows.Devices.Bluetooth;
using Windows.Devices.Bluetooth.Advertisement;
using Windows.Devices.Bluetooth.GenericAttributeProfile;
using Windows.Devices.Enumeration;
using Windows.Media.Capture;
using Windows.Storage;
using Windows.Storage.Streams;
using GazeInput;


namespace Pilot37_RCCar
{
    /// <summary>
    /// An empty page that can be used on its own or navigated to within a Frame.
    /// </summary>
    public sealed partial class SettingsPage : Page
    {
        public SettingsPage()
        {
            this.InitializeComponent();
        }

        private void Settings_Button_Click(object sender, RoutedEventArgs e)
        {
            var _button = sender as Button;
            if (_button != null)
            {
                switch (_button.Name)
                {
                    case "ExitButton":
                        ExitPress();
                        break;
                    case "CloseButton":
                        ClosePress();
                        break;
                    case "SaveButton":
                        ClosePress();
                        break;
                }
            }
        }

        protected override void OnNavigatedTo(NavigationEventArgs e)
        {
            Globals._gaze.GazePointerEvent += OnGazePointerEvent;
            base.OnNavigatedTo(e);
        }

        protected override void OnNavigatedFrom(NavigationEventArgs e)
        {
            Globals._gaze.GazePointerEvent -= OnGazePointerEvent;
            base.OnNavigatedFrom(e);
        }

        private void OnGazePointerEvent(GazePointer sender, GazePointerEventArgs ea)
        {
            // Figure out what part of the GUI is currently being gazed upon
            UIElement _temp = ea.HitTarget;

            // Eyes Off event
            if (_temp == null)
            {
                Debug.WriteLine("Settings: Eyes Off Event!");
            }
            // Button Selection event
            var _button = _temp as Button;
            if (_button != null)
            {
                switch (ea.State)
                {
                    case GazePointerState.Fixation:
                        break;
                    case GazePointerState.Dwell:
                        switch (_button.Name)
                        {
                            case "ExitButton":
                                ExitPress();
                                break;
                            case "CloseButton":
                                ClosePress();
                                break;
                            case "SaveButton":
                                ClosePress();
                                break;
                        }
                        break;
                }
            }
        }

        private void ClosePress()
        {
            this.Frame.Navigate(typeof(MainPage));
        }

        private void ExitPress()
        {
            // TODO: DisconnectBLE() from this Page
            Application.Current.Exit();
        }
    }
}
