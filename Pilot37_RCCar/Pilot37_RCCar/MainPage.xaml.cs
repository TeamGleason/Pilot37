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
        private MediaCapture _mediacCapture1;
        private DeviceInformation _fpvDevice1;
        private CaptureElement _frontCam;

        private BluetoothLEDevice _nordic = null;
        
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
        private bool _paused = false;
        private bool _controlsEnabled = false;
        private int _ticks;

        private const byte SOFT_LEFT_1 = 0x2c;
        private const byte SOFT_LEFT_2 = 0x06;
        private const byte SHARP_LEFT_1 = 0xd0;
        private const byte SHARP_LEFT_2 = 0x07;
        private const byte SOFT_RIGHT_1 = 0x89;
        private const byte SOFT_RIGHT_2 = 0x05;
        private const byte SHARP_RIGHT_1 = 0xe8;
        private const byte SHARP_RIGHT_2 = 0x03;
        private const byte NEUTRAL_1 = 0xdc;
        private const byte NEUTRAL_2 = 0x05;
        private const byte SLOWEST_SPEED_1 = 0x20;
        private const byte SLOWEST_SPEED_2 = 0x06;
        private const byte SLOW_SPEED_1 = 0x30;
        private const byte SLOW_SPEED_2 = 0x06;
        private const byte MEDIUM_SPEED_1 = 0x40;
        private const byte MEDIUM_SPEED_2 = 0x06;
        private const byte FAST_SPEED_1 = 0x72;
        private const byte FAST_SPEED_2 = 0x06;
        private const byte REVERSE_SPEED_1 = 0x5f;
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
            _previousButton = StopButton;
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
            _gaze.Filter = new OneEuroFilter();

            _gaze.GazePointerEvent += OnGazePointerEvent;
            _gaze.EyesOffDelay = 250000;
        }

        private void Application_Suspending(object sender, object o)
        {
            DisconnectFromBLE();
        }

        private void Application_CatchUnhandledException(object sender, UnhandledExceptionEventArgs e)
        {
            // TODO: Make this actually work!
            Debug.WriteLine("Unhandled Exception Caught!");
            DisconnectFromBLE();
        }

        private void DisconnectFromBLE()
        {
            // Place the RC Car into a safe STOP state
            ChangePathFill(_previousButton, _navDefault);
            _previousButton = StopButton;
            StopPath.Fill = _gazedUponStop;
            StateChange(ControlStates.Stop, StopPress);

            // Dispose all BLE related variables
            if (_alive != null)
            {
                _alive.Dispose();
                _alive = null;
            }

            if (_heartBeatCharacteristic != null)
            {
                if (_heartBeatCharacteristic.Service != null)
                {
                    _heartBeatCharacteristic.Service.Dispose();
                }
            }

            _heartBeatCharacteristic = null;
            _GPIOCharacteristic = null;
            _PWMCharacteristic = null;

            if (_primaryService != null)
            {
                _primaryService.Dispose();
            }
            _primaryService = null;

            if (_nordic != null)
            {
                _nordic.Dispose();
            }
            _nordic = null;
    }

        #region Gaze Event
        private void OnGazePointerEvent(GazePointer sender, GazePointerEventArgs ea)
        {
            if (!_controlsEnabled)
            {
                return;
            }

            // Figure out what part of the GUI is currently being gazed upon
            UIElement _temp = ea.HitTarget;

            // Eyes Off event
            if (_temp == null)
            {
                Debug.WriteLine("Eyes Off Event!");
                ChangePathFill(_previousButton, _navDefault);
                _previousButton = StopButton;
                StopPath.Fill = _gazedUponStop;
                StateChange(ControlStates.Stop, StopPress);
            }
            // Button Selection event
            else if (_temp.ToString().Contains("Button"))
            {
                Button _button = (Button)ea.HitTarget;

                switch (ea.State)
                {
                    case GazePointerState.Fixation:
                        if (_button.Equals(PauseButton))
                        {
                            return;
                        }
                        Button_Handler(_button);
                        break;
                    case GazePointerState.Dwell:
                        if (_button.Equals(PauseButton))
                        {
                            PausePress();
                        }
                        break;
                }
            }
        }

        // Highlight the button that is currently gazed upon, set _previous, and trigger the appropriate callback
        private void Button_Handler(Button b)
        {
            if (_paused)
            {
                // Dont allow for any controls if the app is paused
                return;
            }

            if (_previousButton != null && _previousButton != b)
            {
                ChangePathFill(_previousButton, _navDefault);
            }

            ChangePathFill(b, _gazedUpon);
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

            // Create a watcher to find a BLE Device with name "Pilot 37"
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

            _nordic.ConnectionStatusChanged += NordicDevice_ConnectionChange;
            var _nordicServices = await _nordic.GetGattServicesAsync();
            var services = _nordicServices.Services;

            if (services == null)
            {
                Debug.WriteLine("Error: Services from BLE Device can not be ascertained!");
                return;
            }

            foreach (GattDeviceService service in services)
            {
                if (service.Uuid.Equals(_primaryServiceUUID))
                {
                    _primaryService = service;
                }
                var _nordicChars = await service.GetCharacteristicsAsync();
                var characteristics = _nordicChars.Characteristics;

                foreach (GattCharacteristic character in characteristics)
                {
                    SetEachCharacteristic(character);
                }
            }

            // Now that we are connected, begin our Heartbeat communication
            _controlsEnabled = true;
            Debug.WriteLine("BLE Connection Status: CONNECTED");
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
        }

        private void BLEWatcher_Stopped(BluetoothLEAdvertisementWatcher sender, BluetoothLEAdvertisementWatcherStoppedEventArgs args)
        {
            Debug.WriteLine("BLE Watcher has STOPPED");
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
            if (_controlsEnabled)
            {
                Debug.WriteLine("BLE Connection Status: DISCONNECTED");
                _controlsEnabled = false;
            }

            // Brief Delay
            _ticks = Environment.TickCount;
            while (Environment.TickCount - _ticks < 1000);
        }

        private async void keepAlive()
        {
            if (_controlsEnabled)
            {
                await _heartBeatCharacteristic.WriteValueAsync((new byte[] { 1 }).AsBuffer());
            }
        }

        private async Task InitializeCameraAsync()
        {
            if (_mediacCapture1 == null)
            {
                DeviceInformationCollection cameraDevices = await DeviceInformation.FindAllAsync(DeviceClass.VideoCapture);
                _fpvDevice1 = cameraDevices.First();

                // TODO: Make 2 Cameras Work Eventually
                foreach (DeviceInformation device in cameraDevices)
                {
                    if (device.Name.Contains("USB2.0 PC CAMERA"))
                    {
                        _fpvDevice1 = device;

                    }
                }

                _mediacCapture1 = new MediaCapture();
                await _mediacCapture1.InitializeAsync(new MediaCaptureInitializationSettings { VideoDeviceId = _fpvDevice1.Id });
                Preview1.Source = _mediacCapture1;
                _frontCam = Preview1;
                await _frontCam.Source.StartPreviewAsync();
            }
        }

        #region Button Callbacks
        private void Button_Click(object sender, RoutedEventArgs e)
        {
            if (sender.ToString().Contains("Button"))
            {
                Button _button = (Button)sender;

                if (_button == PauseButton)
                {
                    PausePress();
                    return;
                }

                Button_Handler(_button);
            }
        }

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
            // Steering Control
            await _PWMCharacteristic.WriteValueAsync((new byte[] { NEUTRAL_1, NEUTRAL_2, SLOW_SPEED_1, SLOW_SPEED_2 }).AsBuffer());
            //_control = new System.Threading.Timer(state => { SendBLEControl(someForwardArg); }, null, 0, _controlPeriod);
        }

        private async void MediumForwardPress()
        {
            Debug.WriteLine("Medium Forward Press\n");
            // Steering Control
            await _PWMCharacteristic.WriteValueAsync((new byte[] { NEUTRAL_1, NEUTRAL_2, MEDIUM_SPEED_1, MEDIUM_SPEED_2 }).AsBuffer());
            //_control = new System.Threading.Timer(state => { SendBLEControl(someForwardArg); }, null, 0, _controlPeriod);
        }

        private async void FastForwardPress()
        {
            Debug.WriteLine("Fast Forward Press\n");
            // Steering Control
            await _PWMCharacteristic.WriteValueAsync((new byte[] { NEUTRAL_1, NEUTRAL_2, FAST_SPEED_1, FAST_SPEED_2 }).AsBuffer());
            //_control = new System.Threading.Timer(state => { SendBLEControl(someForwardArg); }, null, 0, _controlPeriod);
        }

        private async void SlowSharpRightPress()
        {
            Debug.WriteLine("Slow Sharp Right Press\n");
            // Steering Control
            await _PWMCharacteristic.WriteValueAsync((new byte[] { SHARP_RIGHT_1, SHARP_RIGHT_2, SLOWEST_SPEED_1, SLOWEST_SPEED_2 }).AsBuffer());
            //_control = new System.Threading.Timer(state => { SendBLEControl(someForwardLeftArg); }, null, 0, _controlPeriod);
        }
        private async void SlowSoftRightPress()
        {
            Debug.WriteLine("Slow Soft Right Press\n");
            // Steering Control
            await _PWMCharacteristic.WriteValueAsync((new byte[] { SOFT_RIGHT_1, SOFT_RIGHT_2, SLOW_SPEED_1, SLOW_SPEED_2 }).AsBuffer());
            //_control = new System.Threading.Timer(state => { SendBLEControl(someForwardLeftArg); }, null, 0, _controlPeriod);
        }

        private async void SlowSharpLeftPress()
        {
            Debug.WriteLine("Slow Sharp Left Press\n");
            // Steering Control
            await _PWMCharacteristic.WriteValueAsync((new byte[] { SHARP_LEFT_1, SHARP_LEFT_2, SLOWEST_SPEED_1, SLOWEST_SPEED_2 }).AsBuffer());
            //_control = new System.Threading.Timer(state => { SendBLEControl(someForwardRightArg); }, null, 0, _controlPeriod);
        }

        private async void SlowSoftLeftPress()
        {
            Debug.WriteLine("Slow Soft Left Press\n");
            // Steering Control
            await _PWMCharacteristic.WriteValueAsync((new byte[] { SOFT_LEFT_1, SOFT_LEFT_2, SLOW_SPEED_1, SLOW_SPEED_2 }).AsBuffer());
            //_control = new System.Threading.Timer(state => { SendBLEControl(someForwardRightArg); }, null, 0, _controlPeriod);
        }

        private async void MediumSharpRightPress()
        {
            Debug.WriteLine("Medium Sharp Right Press\n");
            // Steering Control
            await _PWMCharacteristic.WriteValueAsync((new byte[] { SHARP_RIGHT_1, SHARP_RIGHT_2, MEDIUM_SPEED_1, MEDIUM_SPEED_2 }).AsBuffer());
            //_control = new System.Threading.Timer(state => { SendBLEControl(someForwardLeftArg); }, null, 0, _controlPeriod);
        }
        private async void MediumSoftRightPress()
        {
            Debug.WriteLine("Medium Soft Right Press\n");
            // Steering Control
            await _PWMCharacteristic.WriteValueAsync((new byte[] { SOFT_RIGHT_1, SOFT_RIGHT_2, MEDIUM_SPEED_1, MEDIUM_SPEED_2 }).AsBuffer());
            //_control = new System.Threading.Timer(state => { SendBLEControl(someForwardLeftArg); }, null, 0, _controlPeriod);
        }

        private async void MediumSharpLeftPress()
        {
            Debug.WriteLine("Medium Sharp Left Press\n");
            // Steering Control
            await _PWMCharacteristic.WriteValueAsync((new byte[] { SHARP_LEFT_1, SHARP_LEFT_2, MEDIUM_SPEED_1, MEDIUM_SPEED_2 }).AsBuffer());
            //_control = new System.Threading.Timer(state => { SendBLEControl(someForwardRightArg); }, null, 0, _controlPeriod);
        }

        private async void MediumSoftLeftPress()
        {
            Debug.WriteLine("Medium Soft Left Press\n");
            // Steering Control
            await _PWMCharacteristic.WriteValueAsync((new byte[] { SOFT_LEFT_1, SOFT_LEFT_2, MEDIUM_SPEED_1, MEDIUM_SPEED_2 }).AsBuffer());
            //_control = new System.Threading.Timer(state => { SendBLEControl(someForwardRightArg); }, null, 0, _controlPeriod);
        }

        private async void FastSharpRightPress()
        {
            Debug.WriteLine("Fast Sharp Right Press\n");
            // Steering Control
            await _PWMCharacteristic.WriteValueAsync((new byte[] { SHARP_RIGHT_1, SHARP_RIGHT_2, FAST_SPEED_1, FAST_SPEED_2 }).AsBuffer());
            //_control = new System.Threading.Timer(state => { SendBLEControl(someForwardLeftArg); }, null, 0, _controlPeriod);
        }
        private async void FastSoftRightPress()
        {
            Debug.WriteLine("Fast Soft Right Press\n");
            // Steering Control
            await _PWMCharacteristic.WriteValueAsync((new byte[] { SOFT_RIGHT_1, SOFT_RIGHT_2, FAST_SPEED_1, FAST_SPEED_2 }).AsBuffer());
            //_control = new System.Threading.Timer(state => { SendBLEControl(someForwardLeftArg); }, null, 0, _controlPeriod);
        }

        private async void FastSharpLeftPress()
        {
            Debug.WriteLine("Fast Sharp Left Press\n");
            // Steering Control
            await _PWMCharacteristic.WriteValueAsync((new byte[] { SHARP_LEFT_1, SHARP_LEFT_2, FAST_SPEED_1, FAST_SPEED_2 }).AsBuffer());
            //_control = new System.Threading.Timer(state => { SendBLEControl(someForwardRightArg); }, null, 0, _controlPeriod);
        }

        private async void FastSoftLeftPress()
        {
            Debug.WriteLine("Fast Soft Left Press\n");
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
            // Steering Control
            await _PWMCharacteristic.WriteValueAsync((new byte[] { NEUTRAL_1, NEUTRAL_2, REVERSE_SPEED_1, REVERSE_SPEED_2 }).AsBuffer());
        }

        private async void PausePress()
        {
            if (_controlsEnabled)
            {
                // Steering Control
                await _PWMCharacteristic.WriteValueAsync((new byte[] { NEUTRAL_1, NEUTRAL_2, NEUTRAL_1, NEUTRAL_2 }).AsBuffer());
                //_control = new System.Threading.Timer(state => { SendBLEControl(someStopArg); }, null, 0, _controlPeriod);
            }

            _paused = !_paused;

            if (_previousButton != null && _previousButton != PauseButton)
            {
                ChangePathFill(_previousButton, _navDefault);
            }

            StopPath.Fill = _gazedUponStop;

            if (_paused)
            {
                PauseButton.Background = _gazedUpon;
                _previousButton = PauseButton;
            } else
            {
                PauseButton.Background = _sideDefault;
                _previousButton = StopButton;
                StateChange(ControlStates.Stop, StopPress);
            }
        }
        #endregion
    }
}
