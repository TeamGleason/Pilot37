using System;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Diagnostics;
using System.Runtime.InteropServices.WindowsRuntime;
using Windows.Foundation;
using Windows.Foundation.Collections;
using Windows.UI;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Controls.Primitives;
using Windows.UI.Xaml.Data;
using Windows.UI.Xaml.Input;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Navigation;
using Windows.Devices.Bluetooth;
using Windows.Devices.Bluetooth.Advertisement;
using Windows.Devices.Bluetooth.GenericAttributeProfile;
using Windows.Devices.Enumeration;
using Windows.Media.Capture;
using Windows.Storage.Streams;
using GazeInput;

// The Blank Page item template is documented at https://go.microsoft.com/fwlink/?LinkId=402352&clcid=0x409

namespace Pilot37_App
{
    /// <summary>
    /// An empty page that can be used on its own or navigated to within a Frame.
    /// </summary>
    public sealed partial class MainPage : Page
    {
        private GazePointer _gaze;

        private BluetoothLEAdvertisementWatcher bleWatch1, bleWatch2;
        private MediaCapture _mediaCapture;
        private DeviceInformation _fpvDevice1;

        private BluetoothLEDevice _nordic = null;
        private GattDeviceServicesResult _nordicServices = null;
        private GattCharacteristicsResult _nordicChars = null;
        private GattReadResult _nordicReadVal = null;
        private static Button _previous;

        SolidColorBrush _navDefault = new SolidColorBrush(Colors.AliceBlue);
        SolidColorBrush _sideDefault = new SolidColorBrush(Colors.Blue);
        SolidColorBrush _gazedUpon = new SolidColorBrush(Colors.Lime);
        SolidColorBrush _gazedUponStop = new SolidColorBrush(Colors.Red);

        private bool _hrmInitialized = false;
        private bool _hrmDisplay = false;



        public MainPage()
        {
            InitializeComponent();
            Loaded += new RoutedEventHandler(MainPage_OnLoaded);
            Application.Current.Resuming += Application_Resuming;
        }

        private void MainPage_OnLoaded(object sender, RoutedEventArgs e)
        {
            // Set up the a GazePointer object and make it visible on screen
            _gaze = new GazePointer(this);
            _gaze.CursorRadius = 6;
            _gaze.IsCursorVisible = true;

            _gaze.GazePointerEvent += OnGazePointerEvent;
        }

        #region Gaze Event
        private void OnGazePointerEvent(GazePointer sender, GazePointerEventArgs ea)
        {
            // Figure out what part of the GUI is currently being gazed upon
            UIElement _temp = ea.HitTarget;

            // When a new GazePointerEvent occurs, revert the previously selected object to its default (ie, non-gazed upon) appearance
            if (_previous != null)
            {
                if (_previous.Name.Equals("TestButton") || _previous.Name.Equals("SettingsButton"))
                {
                    _previous.Background = _sideDefault;
                } else
                {
                    _previous.Background = _navDefault;
                }
            }

            // Pass the button that was selected to the event handler to determine the appropriate action
            if (_temp.ToString().Contains("Button"))
            {
                Button _button = (Button)ea.HitTarget;
                GazeButton_Handler(_button);
            }

        }

        private void GazeButton_Handler(Button b)
        {
            // Highlight the button that is currently gazed upon, set _previous, and trigger the appropriate callback
            b.Background = _gazedUpon;
            _previous = b;

            switch (b.Name)
            {
                case "ForwardButton":
                    ForwardPress();
                    break;
                case "ForwardLeftButton":
                    ForwardLeftPress();
                    break;
                case "ForwardRightButton":
                    ForwardRightPress();
                    break;
                case "BackwardLeftButton":
                    BackwardLeftPress();
                    break;
                case "BackwardRightButton":
                    BackwardRightPress();
                    break;
                case "StopButton":
                    b.Background = _gazedUponStop;
                    StopPress();
                    break;
                case "ReverseButton":
                    ReversePress();
                    break;
                case "TestButton":
                    TestButtonPress();
                    break;

            }
        }
        #endregion

        private async void Application_Resuming(object sender, object o)
        {
            await InitializeCameraAsync();
        }

        protected async override void OnNavigatedTo(NavigationEventArgs e)
        {
            await InitializeCameraAsync();

            // Create a watcher to find a BLE Device with name "corten" or "Nordic_HRM"
            //TODO: Make a filter with the Manufacturer Data/Device ID, not a LocalName string
            BluetoothLEAdvertisement _bleAdv1 = new BluetoothLEAdvertisement();
            _bleAdv1.LocalName = "corten";
            BluetoothLEAdvertisementFilter _bleAdvFilter1 = new BluetoothLEAdvertisementFilter();
            _bleAdvFilter1.Advertisement = _bleAdv1;
            bleWatch1 = new BluetoothLEAdvertisementWatcher(_bleAdvFilter1);

            bleWatch1.Received += BLEWatcher_Received;
            bleWatch1.Stopped += BLEWatcher_Stopped;
            bleWatch1.Start();

            BluetoothLEAdvertisement _bleAdv2 = new BluetoothLEAdvertisement();
            _bleAdv2.LocalName = "Nordic_HRM";
            BluetoothLEAdvertisementFilter _bleAdvFilter2 = new BluetoothLEAdvertisementFilter();
            _bleAdvFilter2.Advertisement = _bleAdv2;
            bleWatch2 = new BluetoothLEAdvertisementWatcher(_bleAdvFilter2);

            bleWatch2.Received += BLEWatcher_Received;
            bleWatch2.Stopped += BLEWatcher_Stopped;
            bleWatch2.Start();

            base.OnNavigatedTo(e);

            //   +=============================================+
            //   |   Bluetooth LE Connection Pop-Up Selection  |
            //   +=============================================+
            //
            //
            //DevicePicker picker = new DevicePicker();
            //picker.Filter.SupportedDeviceSelectors.Add(BluetoothLEDevice.GetDeviceSelectorFromPairingState(false));
            //picker.Filter.SupportedDeviceSelectors.Add(BluetoothLEDevice.GetDeviceSelectorFromPairingState(true));
            //picker.Show(new Rect(0, 0, 100, 500));
        }

        protected override void OnNavigatedFrom(NavigationEventArgs e)
        {
            bleWatch1.Stop();
            bleWatch2.Stop();
            base.OnNavigatedFrom(e);
        }

        private async void BLEWatcher_Received(BluetoothLEAdvertisementWatcher sender, BluetoothLEAdvertisementReceivedEventArgs args)
        {
            _nordic = await BluetoothLEDevice.FromBluetoothAddressAsync(args.BluetoothAddress);
            Debug.WriteLine($"\nBLE Device Found\n\tName: {_nordic.Name}\n\tID: {_nordic.DeviceId}\n\tAddress: {_nordic.BluetoothAddress}\n");

            if (_nordic == null)
            {
                Debug.WriteLine("Error: BLE Device can't connect!");
                return;
            }

            _nordic.ConnectionStatusChanged += NordicDevice_ConnectionChange;
            _nordicServices = await _nordic.GetGattServicesAsync();
            var services = _nordicServices.Services;

            if (services == null)
            {
                Debug.WriteLine("Error: Services from BLE Device can not be ascertained!");
            }
            else
            {
                Debug.WriteLine($"\nThe BLE Device named {_nordic.Name} has the following {services.Count()} services:");
            }

            foreach (GattDeviceService service in services)
            {
                Debug.WriteLine($"\tService: {service.Uuid}");
                _nordicChars = await service.GetCharacteristicsAsync();
                var characteristics = _nordicChars.Characteristics;

                foreach (GattCharacteristic character in characteristics)
                {
                    Debug.WriteLine($"\t\t- Characteristic: {character.Uuid}");
                }
            }
        }

        private void BLEWatcher_Stopped(BluetoothLEAdvertisementWatcher sender, BluetoothLEAdvertisementWatcherStoppedEventArgs args)
        {
            Debug.WriteLine("STOPPED: Test! Should only print when BLE is advertising!");
        }

        private async void InitializeHRMRead()
        {
            // TODO: Make example of a write to the BLE device 
            //  Example of READING Noric_HRM values: Check hrm_ValueChanged() method below for more
            foreach (GattDeviceService service in _nordicServices.Services)
            {
                _nordicChars = await service.GetCharacteristicsAsync();
                foreach (GattCharacteristic characteristic in _nordicChars.Characteristics)
                {
                    if (characteristic.Uuid.Equals(new Guid("00002a37-0000-1000-8000-00805f9b34fb")))
                    {
                        Debug.WriteLine("\nHeart Rate Measurement Characteristic:");
                        // Enable Notifications for this HRM Characteristic
                        await characteristic.WriteClientCharacteristicConfigurationDescriptorAsync(GattClientCharacteristicConfigurationDescriptorValue.Notify);
                        characteristic.ValueChanged += hrm_ValueChanged;
                    }
                }
            }
        }

        // Invoked every time the HRM BLE Device updates its HRM value, then prints the updated value
        private async void hrm_ValueChanged(GattCharacteristic sender, GattValueChangedEventArgs args)
        {
            if (_hrmDisplay)
            {
                // Note: Must use "BluetoothCacheMode.Uncached" to observe the changing data
                // sender.ProtectionLevel = GattProtectionLevel.Plain;
                GattCharacteristicProperties properties = sender.CharacteristicProperties;
                if (!properties.HasFlag(GattCharacteristicProperties.Read))
                {
                    Debug.WriteLine("READING is NOT supported by this device");
                }

                _nordicReadVal = await sender.ReadValueAsync(BluetoothCacheMode.Uncached);
                if (_nordicReadVal.Value != null)
                {
                    byte[] data = new byte[_nordicReadVal.Value.Length];
                    DataReader.FromBuffer(_nordicReadVal.Value).ReadBytes(data);
                    Debug.WriteLine(data[1]);
                } else
                {
                    Debug.WriteLine($"The HRM value has changed, but cannot be read from {_nordic.Name}");
                    Debug.WriteLine(_nordicReadVal.Status);
                    Debug.WriteLine(_nordicReadVal.ProtocolError);
                }
            }
        }

        // Invoked every time the BLE device connects or disconnects
        private void NordicDevice_ConnectionChange(BluetoothLEDevice sender, object args)
        {
            Debug.WriteLine("Connection status of the BLE device has changed!");
        }

        private async Task InitializeCameraAsync()
        {
            if (_mediaCapture == null)
            {
                DeviceInformationCollection cameraDevices = await DeviceInformation.FindAllAsync(DeviceClass.VideoCapture);
                _fpvDevice1 = cameraDevices.Last();
                // TODO: Grab 2 camera devices for the RC Car, and only 1 for the Drone
                foreach (DeviceInformation device in cameraDevices)
                {
                    if (device.Name.Contains("USB2.0 PC CAMERA"))
                    {
                        _fpvDevice1 = device;
                    } else
                    {
                        Debug.WriteLine("Error: No external video stream detected!");
                    }
                }

                _mediaCapture = new MediaCapture();
                await _mediaCapture.InitializeAsync(new MediaCaptureInitializationSettings { VideoDeviceId = _fpvDevice1.Id });

                // Set the preview source for the CaptureElement. PreviewControl is defined in the xaml file
                PreviewControl.Source = _mediaCapture;
                await _mediaCapture.StartPreviewAsync();
                //await _mediaCapture.StopPreviewAsync();
            }
        }

        #region Click Handling
        private void ForwardLeft_Click(object sender, RoutedEventArgs e)
        {
            ForwardLeftPress();
        }

        private void Forward_Click(object sender, RoutedEventArgs e)
        {
            ForwardPress();
        }

        private void ForwardRight_Click(object sender, RoutedEventArgs e)
        {
            ForwardRightPress();
        }

        private void BackwardRight_Click(object sender, RoutedEventArgs e)
        {
            BackwardRightPress();
        }

        private void Stop_Click(object sender, RoutedEventArgs e)
        {
            StopPress();
        }

        private void BackwardLeft_Click(object sender, RoutedEventArgs e)
        {
            BackwardLeftPress();
        }

        private void Reverse_Click(object sender, RoutedEventArgs e)
        {
            ReversePress();
        }

        private void Settings_Click(object sender, RoutedEventArgs e)
        {
            SettingsPress();
        }

        private void TestButton_OnClick(object sender, RoutedEventArgs e)
        {
            TestButtonPress();
        }
        #endregion

        #region Button Callbacks
        private void ForwardPress()
        {
            Debug.WriteLine("Forward Press\n");
            // Implement
        }

        private void ForwardLeftPress()
        {
            // Implement
        }

        private void ForwardRightPress()
        {
            // Implement
        }

        private void BackwardLeftPress()
        {
            Debug.WriteLine("Backward Left Press\n");
            // Implement
        }

        private void BackwardRightPress()
        {
            // Implement
        }

        private void StopPress()
        {
            // Implement
        }

        private void ReversePress()
        {
            // Implement
        }

        private void SettingsPress()
        {
            // Implement
        }

        private void DPadOnButtonPress()
        {
            // Implement
        }

        private void TestButtonPress()
        {
            // First click initializes reading the HRM values
            // Every subsequent click toggles ON/OFF the output viewed in the console
            if (!_hrmInitialized)
            {
                _hrmInitialized = true;
                InitializeHRMRead();
            }

            _hrmDisplay = !_hrmDisplay;
        }
        #endregion
    }
}
