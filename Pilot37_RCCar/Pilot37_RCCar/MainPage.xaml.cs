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
using Windows.Storage;
using Windows.Storage.Streams;
using GazeInput;

// The Blank Page item template is documented at https://go.microsoft.com/fwlink/?LinkId=402352&clcid=0x409

namespace Pilot37_RCCar
{
    /// <summary>
    /// An eye-gaze enabled application to drive an RC car and display an FPV stream from cameras on the car.
    /// .
    /// </summary>
    public sealed partial class MainPage : Page
    {
        private System.Threading.Timer _alive, _control;
        private static int _alivePeriod = 500, _controlPeriod = 3000;

        private GazePointer _gaze;

        private BluetoothLEAdvertisementWatcher bleWatch1;
        private MediaCapture _mediacCapture1, _mediacCapture2;
        private DeviceInformation _fpvDevice1, _fpvDevice2;
        private CaptureElement _frontCam, _backCam;

        private BluetoothLEDevice _nordic = null;
        private GattDeviceServicesResult _nordicServices = null;
        private GattCharacteristicsResult _nordicChars = null;

        private Guid _vehicleIDGUID = new Guid("8e9f3738-d80c-0991-c44a-3dab1a06896c");
        private GattCharacteristic _vehicleIDCharacteristic = null;
        private Guid _heartBeatGUID = new Guid("8e9f3739-d80c-0991-c44a-3dab1a06896c");
        private GattCharacteristic _heartBeatCharacteristic = null;
        private Guid _GPIOGUID = new Guid("8e9f373a-d80c-0991-c44a-3dab1a06896c");
        private GattCharacteristic _GPIOCharacteristic = null;
        private Guid _PWMGUID = new Guid("8e9f373b-d80c-0991-c44a-3dab1a06896c");
        private GattCharacteristic _PWMCharacteristic = null;
        private Guid _primaryServiceUUID = new Guid("8e9f3737-d80c-0991-c44a-3dab1a06896c");
        private GattDeviceService _primaryService = null;

        SolidColorBrush _navDefault = new SolidColorBrush(Colors.AliceBlue);
        SolidColorBrush _sideDefault = new SolidColorBrush(Colors.Blue);
        SolidColorBrush _gazedUpon = new SolidColorBrush(Colors.Lime);
        SolidColorBrush _gazedUponStop = new SolidColorBrush(Colors.Red);
        private static Button _previousButton;
        private ControlStates _previousState;

        private bool _ToggleState = true;
        private bool _controlsEnabled = true;
        private bool _reverseCamEnabled = true;
        private int _camCount = 0;

        private const byte SOFT_LEFT_1 = 0xd6;
        private const byte SOFT_LEFT_2 = 0x06;
        private const byte SHARP_LEFT_1 = 0xd0;
        private const byte SHARP_LEFT_2 = 0x07;
        private const byte SOFT_RIGHT_1 = 0xe2;
        private const byte SOFT_RIGHT_2 = 0x04;
        private const byte SHARP_RIGHT_1 = 0xe8;
        private const byte SHARP_RIGHT_2 = 0x03;
        private const byte NEUTRAL_1 = 0xdc;
        private const byte NEUTRAL_2 = 0x05;
        private const byte SLOW_SPEED_1 = 0x0e;
        private const byte SLOW_SPEED_2 = 0x06;
        private const byte MEDIUM_SPEED_1 = 0x40;
        private const byte MEDIUM_SPEED_2 = 0x06;
        private const byte FAST_SPEED_1 = 0x72;
        private const byte FAST_SPEED_2 = 0x06;
        private const byte REVERSE_SPEED_1 = 0x78;
        private const byte REVERSE_SPEED_2 = 0x05;


        private enum ControlStates
        {
            Stop,
            Reverse,
            SlowForward,
            MediumForward,
            FastForward,
            SlowSoftLeft,
            SlowSharpLeft,
            SlowSoftRight,
            SlowSharpRight,
            MediumSoftLeft,
            MediumSharpLeft,
            MediumSoftRight,
            MediumSharpRight,
            FastSoftLeft,
            FastSharpLeft,
            FastSoftRight,
            FastSharpRight
        }

        ControlStates _controlState = ControlStates.Stop;

        public MainPage()
        {
            InitializeComponent();
            Loaded += new RoutedEventHandler(MainPage_OnLoaded);
            Application.Current.Resuming += Application_Resuming;
            Application.Current.Suspending += Application_Suspending;
            Application.Current.UnhandledException += new UnhandledExceptionEventHandler(Application_CatchUnhandledException);
        }

        private void MainPage_OnLoaded(object sender, RoutedEventArgs e)
        {
            // Set up the a GazePointer object and make it visible on screen
            _gaze = new GazePointer(this);
            _gaze.CursorRadius = 6;
            _gaze.IsCursorVisible = true;

            _gaze.GazePointerEvent += OnGazePointerEvent;
        }

        private void Application_Suspending(object sender, object o)
        {
            if (_nordic != null)
            {
                _nordic.Dispose();
            }
            Debug.WriteLine("Suspending or Closing App!");

        }

        private void Application_CatchUnhandledException(object sender, UnhandledExceptionEventArgs e)
        {
            // TODO: Test that this actually works!
            Debug.WriteLine("Unhandled Exception Caught!");
            if (_nordic != null)
            {
                _nordic.Dispose();
            }
        }

        #region Gaze Event
        private void OnGazePointerEvent(GazePointer sender, GazePointerEventArgs ea)
        {
            // Figure out what part of the GUI is currently being gazed upon
            UIElement _temp = ea.HitTarget;

            // Pass the button that was selected to the event handler to determine the appropriate action
            if (_temp.ToString().Contains("Button"))
            {
                Button _button = (Button)ea.HitTarget;
                Button_Handler(_button);
            }
        }

        // Highlight the button that is currently gazed upon, set _previous, and trigger the appropriate callback
        private void Button_Handler(Button b)
        {
            ChangePathFill(b, _gazedUpon);

            // Update the GUI so that previously selected buttons return to the correct state when unselected
            if (_previousButton != null && _previousButton != b)
            {
                if (_previousButton.Name.Equals("TestButton") || _previousButton.Name.Equals("SettingsButton"))
                {
                    _previousButton.Background = _sideDefault;
                    StopPath.Fill = _navDefault;
                }
                else if (_previousButton.Name.Equals("ReverseCameraButton"))
                {
                    StopPath.Fill = _navDefault;
                }
                else
                {
                    ChangePathFill(_previousButton, _navDefault);
                }
            }

            _previousButton = b;

            switch (b.Name)
            {
                case "SlowForwardButton":
                    StateChange(ControlStates.SlowForward, SlowForwardPress);
                    break;
                case "MediumForwardButton":
                    StateChange(ControlStates.MediumForward, MediumForwardPress);
                    break;
                case "FastForwardButton":
                    StateChange(ControlStates.FastForward, FastForwardPress);
                    break;
                case "SlowSoftLeftButton":
                    StateChange(ControlStates.SlowSoftLeft, SlowSoftLeftPress);
                    break;
                case "SlowSoftRightButton":
                    StateChange(ControlStates.SlowSoftRight, SlowSoftRightPress);
                    break;
                case "SlowSharpLeftButton":
                    StateChange(ControlStates.SlowSharpLeft, SlowSharpLeftPress);
                    break;
                case "SlowSharpRightButton":
                    StateChange(ControlStates.SlowSharpRight, SlowSharpRightPress);
                    break;
                case "MediumSoftLeftButton":
                    StateChange(ControlStates.MediumSoftLeft, MediumSoftLeftPress);
                    break;
                case "MediumSoftRightButton":
                    StateChange(ControlStates.MediumSoftRight, MediumSoftRightPress);
                    break;
                case "MediumSharpLeftButton":
                    StateChange(ControlStates.MediumSharpLeft, MediumSharpLeftPress);
                    break;
                case "MediumSharpRightButton":
                    StateChange(ControlStates.MediumSharpRight, MediumSharpRightPress);
                    break;
                case "FastSoftLeftButton":
                    StateChange(ControlStates.FastSoftLeft, FastSoftLeftPress);
                    break;
                case "FastSoftRightButton":
                    StateChange(ControlStates.FastSoftRight, FastSoftRightPress);
                    break;
                case "FastSharpLeftButton":
                    StateChange(ControlStates.FastSharpLeft, FastSharpLeftPress);
                    break;
                case "FastSharpRightButton":
                    StateChange(ControlStates.FastSharpRight, FastSharpRightPress);
                    break;
                case "StopButton":
                    StateChange(ControlStates.Stop, StopPress);
                    break;
                case "ReverseButton":
                    StateChange(ControlStates.Reverse, ReversePress);
                    break;
                case "TestButton":
                    TestButtonPress();
                    StopButton.Background = _gazedUponStop;
                    StateChange(ControlStates.Stop, StopPress);
                    break;
                case "SettingsButton":
                    StopButton.Background = _gazedUponStop;
                    StateChange(ControlStates.Stop, StopPress);
                    SettingsPress();
                    break;
                case "ReverseCameraButton":
                    StopButton.Background = _gazedUponStop;
                    StateChange(ControlStates.Stop, StopPress);
                    ReverseCameraPress();
                    break;
            }
        }

        private void ChangePathFill(Button b, SolidColorBrush color)
        {
            switch (b.Name)
            {
                case "StopButton":
                    if (color.Equals(_gazedUpon))
                    {
                        StopPath.Fill = _gazedUponStop;
                    } else
                    {
                        StopPath.Fill = color;
                    }
                    break;
                case "ReverseButton":
                    ReversePath.Fill = color;
                    break;
                case "SlowForwardButton":
                    SlowForwardPath.Fill = color;
                    break;
                case "MediumForwardButton":
                    MediumForwardPath.Fill = color;
                    break;
                case "FastForwardButton":
                    FastForwardPath.Fill = color;
                    break;
                case "SlowSoftLeftButton":
                    SlowSoftLeftPath.Fill = color;
                    break;
                case "SlowSoftRightButton":
                    SlowSoftRightPath.Fill = color;
                    break;
                case "SlowSharpLeftButton":
                    SlowSharpLeftPath.Fill = color;
                    break;
                case "SlowSharpRightButton":
                    SlowSharpRightPath.Fill = color;
                    break;
                case "MediumSoftLeftButton":
                    MediumSoftLeftPath.Fill = color;
                    break;
                case "MediumSoftRightButton":
                    MediumSoftRightPath.Fill = color;
                    break;
                case "MediumSharpLeftButton":
                    MediumSharpLeftPath.Fill = color;
                    break;
                case "MediumSharpRightButton":
                    MediumSharpRightPath.Fill = color;
                    break;
                case "FastSoftLeftButton":
                    FastSoftLeftPath.Fill = color;
                    break;
                case "FastSoftRightButton":
                    FastSoftRightPath.Fill = color;
                    break;
                case "FastSharpLeftButton":
                    FastSharpLeftPath.Fill = color;
                    break;
                case "FastSharpRightButton":
                    FastSharpRightPath.Fill = color;
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
            _bleAdv1.LocalName = "Pilot 37";
            BluetoothLEAdvertisementFilter _bleAdvFilter1 = new BluetoothLEAdvertisementFilter();
            _bleAdvFilter1.Advertisement = _bleAdv1;
            bleWatch1 = new BluetoothLEAdvertisementWatcher(_bleAdvFilter1);

            bleWatch1.Received += BLEWatcher_Received;
            bleWatch1.Stopped += BLEWatcher_Stopped;
            bleWatch1.Start();

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
            base.OnNavigatedFrom(e);
        }

        private async void BLEWatcher_Received(BluetoothLEAdvertisementWatcher sender, BluetoothLEAdvertisementReceivedEventArgs args)
        {
            _nordic = await BluetoothLEDevice.FromBluetoothAddressAsync(args.BluetoothAddress);

            if (_nordic == null)
            {
                Debug.WriteLine("Error: BLE Device can't connect!");
                return;
            }

            Debug.WriteLine($"\nBLE Device Found\n\tName: {_nordic.Name}\n\tID: {_nordic.DeviceId}\n\tAddress: {_nordic.BluetoothAddress}\n");

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
                if (service.Uuid.Equals(_primaryServiceUUID))
                {
                    _primaryService = service;
                }

                Debug.WriteLine($"\tService: {service.Uuid}");
                _nordicChars = await service.GetCharacteristicsAsync();
                var characteristics = _nordicChars.Characteristics;

                foreach (GattCharacteristic character in characteristics)
                {
                    SetEachCharacteristic(character);

                    Debug.WriteLine($"\t\t- Characteristic: {character.Uuid}");
                }
            }

            _alive = new System.Threading.Timer(state => { keepAlive(); }, null, 0, _alivePeriod);
        }

        private void SetEachCharacteristic(GattCharacteristic c)
        {
            if (c.Uuid.Equals(_heartBeatGUID))
            {
                _heartBeatCharacteristic = c;
            }

            if (c.Uuid.Equals(_GPIOGUID))
            {
                _GPIOCharacteristic = c;
            }

            if (c.Uuid.Equals(_PWMGUID))
            {
                _PWMCharacteristic = c;
            }

            if (c.Uuid.Equals(_vehicleIDGUID))
            {
                _vehicleIDCharacteristic = c;
            }
        }

        private void BLEWatcher_Stopped(BluetoothLEAdvertisementWatcher sender, BluetoothLEAdvertisementWatcherStoppedEventArgs args)
        {
            Debug.WriteLine("STOPPED: Test! Should only print when BLE is advertising!");
        }

        private async void ToggleLED()
        {
            if (_ToggleState)
            {
                await _GPIOCharacteristic.WriteValueAsync((new byte[] { 0 }).AsBuffer());
            }
            else
            {
                await _GPIOCharacteristic.WriteValueAsync((new byte[] { 1 }).AsBuffer());
            }

            _ToggleState = !_ToggleState;
        }


        // Invoked every time the BLE device connects or disconnects
        private void NordicDevice_ConnectionChange(BluetoothLEDevice sender, object args)
        {
            Debug.WriteLine("Connection status of the BLE device has changed!");
        }

        private async void keepAlive()
        {
            await _heartBeatCharacteristic.WriteValueAsync((new byte[] { 1 }).AsBuffer());
        }

        private async Task InitializeCameraAsync()
        {
            if (_mediacCapture1 == null && _mediacCapture2 == null)
            {
                DeviceInformationCollection cameraDevices = await DeviceInformation.FindAllAsync(DeviceClass.VideoCapture);
                _fpvDevice1 = cameraDevices.First();
                _fpvDevice2 = cameraDevices.Last();

                // TODO: Make 2 Cameras Work!
                foreach (DeviceInformation device in cameraDevices)
                {
                    if (device.Name.Contains("USB2.0 PC CAMERA"))
                    {
                        Debug.WriteLine($"{device.Name.ToString()}");
                        if (_camCount == 1)
                        {
                            _fpvDevice2 = device;
                            Debug.WriteLine("Cam2 Set!");
                        }

                        if (_camCount == 0)
                        {
                            _fpvDevice1 = device;
                            _camCount = 1;
                            Debug.WriteLine("Cam1 Set!");
                        }

                    }
                    else
                    {
                        Debug.WriteLine($"Error: Found {device.Name.ToString()} | No external video stream detected!");
                    }
                }

                _mediacCapture1 = new MediaCapture();
                await _mediacCapture1.InitializeAsync(new MediaCaptureInitializationSettings { VideoDeviceId = _fpvDevice1.Id });
                Preview1.Source = _mediacCapture1;
                _frontCam = Preview1;
                await _frontCam.Source.StartPreviewAsync();


                _mediacCapture2 = new MediaCapture();
                await _mediacCapture2.InitializeAsync(new MediaCaptureInitializationSettings { VideoDeviceId = _fpvDevice2.Id });
                Preview2.Source = _mediacCapture2;
                _backCam = Preview2;
                await _backCam.Source.StartPreviewAsync();
            }
        }

        #region Camera Switching
        private void SwitchCameraToFront()
        {
            if ((_previousState == ControlStates.Reverse || _previousState == ControlStates.Stop))
            {
                _frontCam.Visibility = Visibility.Visible;
                _backCam.Visibility = Visibility.Collapsed;
            }
        }

        private void SwitchCameraToBack()
        {
            if (_reverseCamEnabled)
            {
                _backCam.Visibility = Visibility.Visible;
                _frontCam.Visibility = Visibility.Collapsed;
            }
        }

        private void SwitchCameraView()
        {
            if (_frontCam.Visibility == Visibility.Visible)
            {
                _frontCam.Visibility = Visibility.Collapsed;
                _backCam.Visibility = Visibility.Visible;
            }
            else
            {
                _frontCam.Visibility = Visibility.Visible;
                _backCam.Visibility = Visibility.Collapsed;
            }
        }

        private void SwapCameras()
        {
            SwitchCameraView();
            CaptureElement _placeHolder = _frontCam;
            _frontCam = _backCam;
            _backCam = _placeHolder;
        }
        #endregion

        private void Button_Click(object sender, RoutedEventArgs e)
        {
            if (sender.ToString().Contains("Button"))
            {
                Button _button = (Button)sender;
                Button_Handler(_button);
            }
        }

        #region Button Callbacks
        private void StateChange(ControlStates state, Action func)
        {
            if (_controlState != state && _controlsEnabled)
            {
                _previousState = _controlState;
                _controlState = state;
                func();
            }
        }

        private async void SlowForwardPress()
        {
            Debug.WriteLine("Slow Forward Press\n");
            SwitchCameraToFront();
            // Steering Control
            await _PWMCharacteristic.WriteValueAsync((new byte[] { NEUTRAL_1, NEUTRAL_2, SLOW_SPEED_1, SLOW_SPEED_2 }).AsBuffer());
            //_control = new System.Threading.Timer(state => { SendBLEControl(someForwardArg); }, null, 0, _controlPeriod);
        }

        private async void MediumForwardPress()
        {
            Debug.WriteLine("Medium Forward Press\n");
            SwitchCameraToFront();
            // Steering Control
            await _PWMCharacteristic.WriteValueAsync((new byte[] { NEUTRAL_1, NEUTRAL_2, MEDIUM_SPEED_1, MEDIUM_SPEED_2 }).AsBuffer());
            //_control = new System.Threading.Timer(state => { SendBLEControl(someForwardArg); }, null, 0, _controlPeriod);
        }

        private async void FastForwardPress()
        {
            Debug.WriteLine("Fast Forward Press\n");
            SwitchCameraToFront();
            // Steering Control
            await _PWMCharacteristic.WriteValueAsync((new byte[] { NEUTRAL_1, NEUTRAL_2, FAST_SPEED_1, FAST_SPEED_2 }).AsBuffer());
            //_control = new System.Threading.Timer(state => { SendBLEControl(someForwardArg); }, null, 0, _controlPeriod);
        }

        private async void SlowSharpRightPress()
        {
            Debug.WriteLine("Slow Sharp Right Press\n");
            SwitchCameraToFront();
            // Steering Control
            await _PWMCharacteristic.WriteValueAsync((new byte[] { SHARP_RIGHT_1, SHARP_RIGHT_2, SLOW_SPEED_1, SLOW_SPEED_2 }).AsBuffer());
            //_control = new System.Threading.Timer(state => { SendBLEControl(someForwardLeftArg); }, null, 0, _controlPeriod);
        }
        private async void SlowSoftRightPress()
        {
            Debug.WriteLine("Slow Soft Right Press\n");
            SwitchCameraToFront();
            // Steering Control
            await _PWMCharacteristic.WriteValueAsync((new byte[] { SOFT_RIGHT_1, SOFT_RIGHT_2, SLOW_SPEED_1, SLOW_SPEED_2 }).AsBuffer());
            //_control = new System.Threading.Timer(state => { SendBLEControl(someForwardLeftArg); }, null, 0, _controlPeriod);
        }

        private async void SlowSharpLeftPress()
        {
            Debug.WriteLine("Slow Sharp Left Press\n");
            SwitchCameraToFront();
            // Steering Control
            await _PWMCharacteristic.WriteValueAsync((new byte[] { SHARP_LEFT_1, SHARP_LEFT_2, SLOW_SPEED_1, SLOW_SPEED_2 }).AsBuffer());
            //_control = new System.Threading.Timer(state => { SendBLEControl(someForwardRightArg); }, null, 0, _controlPeriod);
        }

        private async void SlowSoftLeftPress()
        {
            Debug.WriteLine("Slow Soft Left Press\n");
            SwitchCameraToFront();
            // Steering Control
            await _PWMCharacteristic.WriteValueAsync((new byte[] { SOFT_LEFT_1, SOFT_LEFT_2, SLOW_SPEED_1, SLOW_SPEED_2 }).AsBuffer());
            //_control = new System.Threading.Timer(state => { SendBLEControl(someForwardRightArg); }, null, 0, _controlPeriod);
        }

        private async void MediumSharpRightPress()
        {
            Debug.WriteLine("Medium Sharp Right Press\n");
            SwitchCameraToFront();
            // Steering Control
            await _PWMCharacteristic.WriteValueAsync((new byte[] { SHARP_RIGHT_1, SHARP_RIGHT_2, MEDIUM_SPEED_1, MEDIUM_SPEED_2 }).AsBuffer());
            //_control = new System.Threading.Timer(state => { SendBLEControl(someForwardLeftArg); }, null, 0, _controlPeriod);
        }
        private async void MediumSoftRightPress()
        {
            Debug.WriteLine("Medium Soft Right Press\n");
            SwitchCameraToFront();
            // Steering Control
            await _PWMCharacteristic.WriteValueAsync((new byte[] { SOFT_RIGHT_1, SOFT_RIGHT_2, MEDIUM_SPEED_1, MEDIUM_SPEED_2 }).AsBuffer());
            //_control = new System.Threading.Timer(state => { SendBLEControl(someForwardLeftArg); }, null, 0, _controlPeriod);
        }

        private async void MediumSharpLeftPress()
        {
            Debug.WriteLine("Medium Sharp Left Press\n");
            SwitchCameraToFront();
            // Steering Control
            await _PWMCharacteristic.WriteValueAsync((new byte[] { SHARP_LEFT_1, SHARP_LEFT_2, MEDIUM_SPEED_1, MEDIUM_SPEED_2 }).AsBuffer());
            //_control = new System.Threading.Timer(state => { SendBLEControl(someForwardRightArg); }, null, 0, _controlPeriod);
        }

        private async void MediumSoftLeftPress()
        {
            Debug.WriteLine("Medium Soft Left Press\n");
            SwitchCameraToFront();
            // Steering Control
            await _PWMCharacteristic.WriteValueAsync((new byte[] { SOFT_LEFT_1, SOFT_LEFT_2, MEDIUM_SPEED_1, MEDIUM_SPEED_2 }).AsBuffer());
            //_control = new System.Threading.Timer(state => { SendBLEControl(someForwardRightArg); }, null, 0, _controlPeriod);
        }

        private async void FastSharpRightPress()
        {
            Debug.WriteLine("Fast Sharp Right Press\n");
            SwitchCameraToFront();
            // Steering Control
            await _PWMCharacteristic.WriteValueAsync((new byte[] { SHARP_RIGHT_1, SHARP_RIGHT_2, FAST_SPEED_1, FAST_SPEED_2 }).AsBuffer());
            //_control = new System.Threading.Timer(state => { SendBLEControl(someForwardLeftArg); }, null, 0, _controlPeriod);
        }
        private async void FastSoftRightPress()
        {
            Debug.WriteLine("Fast Soft Right Press\n");
            SwitchCameraToFront();
            // Steering Control
            await _PWMCharacteristic.WriteValueAsync((new byte[] { SOFT_RIGHT_1, SOFT_RIGHT_2, FAST_SPEED_1, FAST_SPEED_2 }).AsBuffer());
            //_control = new System.Threading.Timer(state => { SendBLEControl(someForwardLeftArg); }, null, 0, _controlPeriod);
        }

        private async void FastSharpLeftPress()
        {
            Debug.WriteLine("Fast Sharp Left Press\n");
            SwitchCameraToFront();
            // Steering Control
            await _PWMCharacteristic.WriteValueAsync((new byte[] { SHARP_LEFT_1, SHARP_LEFT_2, FAST_SPEED_1, FAST_SPEED_2 }).AsBuffer());
            //_control = new System.Threading.Timer(state => { SendBLEControl(someForwardRightArg); }, null, 0, _controlPeriod);
        }

        private async void FastSoftLeftPress()
        {
            Debug.WriteLine("Fast Soft Left Press\n");
            SwitchCameraToFront();
            // Steering Control
            await _PWMCharacteristic.WriteValueAsync((new byte[] { SOFT_LEFT_1, SOFT_LEFT_2, FAST_SPEED_1, FAST_SPEED_2 }).AsBuffer());
            //_control = new System.Threading.Timer(state => { SendBLEControl(someForwardRightArg); }, null, 0, _controlPeriod);
        }

        private async void StopPress()
        {
            Debug.WriteLine("Stop Press\n");
            // Steering Control
            await _PWMCharacteristic.WriteValueAsync((new byte[] { NEUTRAL_1, NEUTRAL_2, NEUTRAL_1, NEUTRAL_2 }).AsBuffer());
            //_control = new System.Threading.Timer(state => { SendBLEControl(someStopArg); }, null, 0, _controlPeriod);
        }

        private async void ReversePress()
        {
            Debug.WriteLine("Reverse Press\n");
            SwitchCameraToBack();
            // Steering Control
            await _PWMCharacteristic.WriteValueAsync((new byte[] { NEUTRAL_1, NEUTRAL_2, REVERSE_SPEED_1, REVERSE_SPEED_2 }).AsBuffer());
            //_control = new System.Threading.Timer(state => { SendBLEControl(someReverseArg); }, null, 0, _controlPeriod);
        }

        private void SettingsPress()
        {
            SwapCameras();
        }

        private void ReverseCameraPress()
        {
            if (_reverseCamEnabled)
            {
                if (_previousState == ControlStates.Reverse)
                {
                    _backCam.Visibility = Visibility.Collapsed;
                    _frontCam.Visibility = Visibility.Visible;
                }
                ReverseCameraButton.Background = new SolidColorBrush(Colors.Red);
            }
            else
            {
                if (_previousState == ControlStates.Reverse)
                {
                    _backCam.Visibility = Visibility.Visible;
                    _frontCam.Visibility = Visibility.Collapsed;
                }
                ReverseCameraButton.Background = new SolidColorBrush(Colors.Green);
            }

            _reverseCamEnabled = !_reverseCamEnabled;
        }

        private void TestButtonPress()
        {
            ToggleLED();
        }
        #endregion
    }
}
