using System;
using System.Threading.Tasks;
using System.Linq;
using System.Diagnostics;
using System.Runtime.InteropServices.WindowsRuntime;
using Windows.UI;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Core;
using Windows.ApplicationModel.Core;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Navigation;
using Windows.Devices.Bluetooth;
using Windows.Devices.Bluetooth.Advertisement;
using Windows.Devices.Bluetooth.GenericAttributeProfile;
using Windows.Devices.Enumeration;
using Windows.Media.Capture;
using Windows.Storage;
using GazeInput;

namespace Pilot37_RCCar
{
    /// <summary>
    /// An eye-gaze enabled application to drive an RC car and display an FPV stream from cameras on the car.
    /// </summary>

    public static class Globals
    {
        public static GazePointer _gaze;
        public static Visibility _donut = Visibility.Collapsed;
        public static Visibility _wave = Visibility.Collapsed;
        public static bool _firstTimeHere = true;
        public static bool _personality = false;
        public static bool _controlsEnabledPrev = false;

        public static BluetoothLEAdvertisement _bleAdv1 = null;
        public static BluetoothLEAdvertisementFilter _bleAdvFilter1 = null;
        public static BluetoothLEAdvertisementWatcher bleWatch1;
        public static BluetoothLEDevice _nordic = null;

        public static Guid _heartBeatGUID = new Guid("8e9f3739-d80c-0991-c44a-3dab1a06896c");
        public static GattCharacteristic _heartBeatCharacteristic = null;
        public static Guid _GPIOGUID = new Guid("8e9f373a-d80c-0991-c44a-3dab1a06896c");
        public static GattCharacteristic _GPIOCharacteristic = null;
        public static Guid _PWMGUID = new Guid("8e9f373b-d80c-0991-c44a-3dab1a06896c");
        public static GattCharacteristic _PWMCharacteristic = null;
        public static Guid _primaryServiceUUID = new Guid("8e9f3737-d80c-0991-c44a-3dab1a06896c");
        public static GattDeviceService _primaryService = null;
        public static Guid _VehicleIDUUID = new Guid("8e9f3738-d80c-0991-c44a-3dab1a06896c");


        public static String _fastForwardSetting = "30%";
        public static String _slowForwardSetting = "30%";
        public static String _reverseSetting = "30%";
        public static String _sharpTurnSetting = "80%";
        public static String _softTurnSetting = "30%";

        public static ApplicationDataContainer _localSettings = ApplicationData.Current.LocalSettings;

        public static byte SOFT_LEFT_1 = 0x27;
        public static byte SOFT_LEFT_2 = 0x06;
        public static byte SHARP_LEFT_1 = 0x6c;
        public static byte SHARP_LEFT_2 = 0x07;
        public static byte SOFT_RIGHT_1 = 0x91;
        public static byte SOFT_RIGHT_2 = 0x05;
        public static byte SHARP_RIGHT_1 = 0x4c;
        public static byte SHARP_RIGHT_2 = 0x04;
        public static byte NEUTRAL_1 = 0xdc;
        public static byte NEUTRAL_2 = 0x05;
        public static byte SLOW_SPEED_1 = 0x27;
        public static byte SLOW_SPEED_2 = 0x06;
        public static byte FAST_SPEED_1 = 0x72;
        public static byte FAST_SPEED_2 = 0x06;
        public static byte REVERSE_SPEED_1 = 0x46;
        public static byte REVERSE_SPEED_2 = 0x05;
        public static byte RECENT_SPEED_1 = SLOW_SPEED_1;
        public static byte RECENT_SPEED_2 = SLOW_SPEED_2;
    }

    public sealed partial class MainPage : Page
    {
        private System.Threading.Timer _alive, _control;
        private static int _alivePeriod = 500, _controlPeriod = 3000;

        private MediaCapture _mediacCapture1;
        private DeviceInformation _fpvDevice1;
        private CaptureElement _frontCam;

        SolidColorBrush _navDefault = new SolidColorBrush(Colors.AliceBlue);
        SolidColorBrush _sideDefault = new SolidColorBrush(Colors.Blue);
        SolidColorBrush _gazedUpon = new SolidColorBrush(Colors.Lime);
        SolidColorBrush _gazedUponStop = new SolidColorBrush(Colors.Red);
        private static Button _previousButton;
        private ControlStates _previousState;
        
        private bool _paused = false;
        private bool _controlsEnabled = false;
        private int _ticks;

        private enum ControlStates
        {
            Stop,
            Reverse,
            SlowForward,
            FastForward,
            SoftLeft,
            SharpLeft,
            SoftRight,
            SharpRight,
            Donut,
            Wave
        }

        ControlStates _controlState = ControlStates.Stop;

        public MainPage()
        {
            InitializeComponent();
            _previousButton = StopButton;
            Loaded += MainPage_OnLoaded;
            Application.Current.Resuming += Application_Resuming;
            Application.Current.Suspending += Application_Suspending;
            Application.Current.UnhandledException += new UnhandledExceptionEventHandler(Application_CatchUnhandledException);
        }

        private void MainPage_OnLoaded(object sender, RoutedEventArgs e)
        {
            // Set up the a GazePointer object and make it visible on screen
            if (Globals._firstTimeHere)
            {
                // TODO: Initilize with "this" and create a new GazePointer on each Page Navigation
                Globals._gaze = new GazePointer(Window.Current.Content);
                Globals._gaze.CursorRadius = 6;
                Globals._gaze.IsCursorVisible = true;
                Globals._gaze.Filter = new OneEuroFilter();

                Globals._gaze.GazePointerEvent += OnGazePointerEvent;
                Globals._gaze.EyesOffDelay = 250000;

                PullSettingsFromStorage();
            }

            SetPersonalityVisibility();
        }

        private void SetPersonalityVisibility()
        {
            if (Globals._personality)
            {
                WaveButton.Visibility = Visibility.Visible;
                DonutButton.Visibility = Visibility.Visible;
            }
            else
            {
                WaveButton.Visibility = Visibility.Collapsed;
                DonutButton.Visibility = Visibility.Collapsed;
            }
        }

        private void PullSettingsFromStorage()
        {
            // Settings - Percentages
            String _ffs = Globals._localSettings.Values["fastForwardSetting"] as String;
            String _sfs = Globals._localSettings.Values["slowForwardSetting"] as String;
            String _rs = Globals._localSettings.Values["reverseSetting"] as String;
            String _shts = Globals._localSettings.Values["sharpTurnSetting"] as String;
            String _sots = Globals._localSettings.Values["softTurnSetting"] as String;
            if (_ffs != null)
            {
                Globals._fastForwardSetting = _ffs;
            }
            if (_sfs != null)
            {
                Globals._slowForwardSetting = _sfs;
            }
            if (_rs != null)
            {
                Globals._reverseSetting = _rs;
            }
            if (_shts != null)
            {
                Globals._sharpTurnSetting = _shts;
            }
            if (_sots != null)
            {
                Globals._softTurnSetting = _sots;
            }

            // Settings - Actual Byte Values
            String _ffv1 = Globals._localSettings.Values["fastForwardValue1"] as String;
            String _ffv2 = Globals._localSettings.Values["fastForwardValue2"] as String;
            String _sfv1 = Globals._localSettings.Values["slowForwardValue1"] as String;
            String _sfv2 = Globals._localSettings.Values["slowForwardValue2"] as String;
            String _rv1 = Globals._localSettings.Values["reverseValue1"] as String;
            String _rv2 = Globals._localSettings.Values["reverseValue2"] as String;

            String _shrv1 = Globals._localSettings.Values["sharpRightValue1"] as String;
            String _shrv2 = Globals._localSettings.Values["sharpRightValue2"] as String;
            String _shlv1 = Globals._localSettings.Values["sharpLeftValue1"] as String;
            String _shlv2 = Globals._localSettings.Values["sharpLeftValue2"] as String;
            String _sorv1 = Globals._localSettings.Values["softRightValue1"] as String;
            String _sorv2 = Globals._localSettings.Values["softRightValue2"] as String;
            String _solv1 = Globals._localSettings.Values["softLeftValue1"] as String;
            String _solv2 = Globals._localSettings.Values["softLeftValue2"] as String;


            if (_ffv1 != null)
            {
                Globals.FAST_SPEED_1 = Convert.ToByte(_ffv1);
            }
            if (_ffv2 != null)
            {
                Globals.FAST_SPEED_2 = Convert.ToByte(_ffv2);
            }
            if (_sfv1 != null)
            {
                Globals.SLOW_SPEED_1 = Convert.ToByte(_sfv1);
            }
            if (_sfv2 != null)
            {
                Globals.SLOW_SPEED_2 = Convert.ToByte(_sfv2);
            }
            if (_rv1 != null)
            {
                Globals.REVERSE_SPEED_1 = Convert.ToByte(_rv1);
            }
            if (_rv2 != null)
            {
                Globals.REVERSE_SPEED_2 = Convert.ToByte(_rv2);
            }

            if (_shrv1 != null)
            {
                Globals.SHARP_RIGHT_1 = Convert.ToByte(_shrv1);
            }
            if (_shrv2 != null)
            {
                Globals.SHARP_RIGHT_2 = Convert.ToByte(_shrv2);
            }
            if (_shlv1 != null)
            {
                Globals.SHARP_LEFT_1 = Convert.ToByte(_shlv1);
            }
            if (_shlv2 != null)
            {
                Globals.SHARP_LEFT_2 = Convert.ToByte(_shlv2);
            }
            if (_sorv1 != null)
            {
                Globals.SOFT_RIGHT_1 = Convert.ToByte(_sorv1);
            }
            if (_sorv2 != null)
            {
                Globals.SOFT_RIGHT_2 = Convert.ToByte(_sorv2);
            }
            if (_solv1 != null)
            {
                Globals.SOFT_LEFT_1 = Convert.ToByte(_solv1);
            }
            if (_solv2 != null)
            {
                Globals.SOFT_LEFT_2 = Convert.ToByte(_solv2);
            }
        }

        private void Application_Suspending(object sender, object o)
        {
            Debug.WriteLine("Suspending");
            DisconnectFromBLE();
        }

        private void Application_CatchUnhandledException(object sender, UnhandledExceptionEventArgs e)
        {
            Exception _exc = e.Exception;
            Debug.WriteLine("\nUnhandled Exception Caught!");
            Debug.WriteLine($"Exception Message: {_exc.Message}");
            Debug.WriteLine($"Exception HRESULT: {_exc.HResult:X}\n");
            Debug.WriteLine($"BLE Watcher Status: {Globals.bleWatch1.Status}\n\n");
            
            switch ((UInt32) _exc.HResult)
            {
                case 0x80000013:
                    e.Handled = true;
                    StopButton.Background = _gazedUponStop;
                    if (_previousButton != null)
                    {
                        if (_previousButton.Name == "PauseButton")
                        {
                            _previousButton.Background = _sideDefault;
                        }
                        else
                        {
                            _previousButton.Background = _navDefault;
                        }
                    }

                    _previousButton = StopButton;
                    _controlState = ControlStates.Stop;
                    _controlsEnabled = false;
                    NotConnected();
                    break;
                default:
                    break;
            }
        }

        private void DisconnectFromBLE()
        {
            // Place the RC Car into a safe STOP state
            if (Globals._nordic != null && Globals._PWMCharacteristic != null)
            {
                Stop();
            }

            Globals._bleAdv1 = null;
            Globals._bleAdvFilter1 = null;

            if (Globals.bleWatch1 != null)
            {
                Globals.bleWatch1.Stop();
                Globals.bleWatch1 = null;
            }

            // Dispose all BLE related variables
            if (_alive != null)
            {
                _alive.Dispose();
                _alive = null;
            }

            if (Globals._heartBeatCharacteristic != null)
            {
                if (Globals._heartBeatCharacteristic.Service != null)
                {
                    Globals._heartBeatCharacteristic.Service.Dispose();
                }
            }

            Globals._heartBeatCharacteristic = null;
            Globals._GPIOCharacteristic = null;
            Globals._PWMCharacteristic = null;

            if (Globals._primaryService != null)
            {
                Globals._primaryService.Dispose();
            }
            Globals._primaryService = null;

            if (Globals._nordic != null)
            {
                Globals._nordic.Dispose();
            }
            Globals._nordic = null;
        }

        #region Gaze Event
        private void OnGazePointerEvent(GazePointer sender, GazePointerEventArgs ea)
        {
            // Figure out what part of the GUI is currently being gazed upon
            UIElement _temp = ea.HitTarget;
            var _button = _temp as Button;

            // Eyes Off event
            if (_temp == null && _controlsEnabled)
            {
                Debug.WriteLine("Eyes Off Event!");
                Stop();
            }
            // Button Selection event
            else if (_button != null)
            {
                switch (ea.State)
                {
                    case GazePointerState.Fixation:
                        if (_button.Equals(PauseButton) || _button.Equals(SettingsButton))
                        {
                            return;
                        }
                        Button_Handler(_button);
                        break;
                    case GazePointerState.Dwell:
                        switch (_button.Name)
                        {
                            case "PauseButton":
                                PausePress();
                                break;
                            case "SettingsButton":
                                SettingsPress();
                                break;
                        }
                        break;
                }
            }
        }

        // Highlight the button that is currently gazed upon, set _previous, and trigger the appropriate callback
        private void Button_Handler(Button b)
        {
            if (_paused || !_controlsEnabled)
            {
                return;
            }

            if (_previousButton != null && _previousButton != b)
            {
                if (_previousButton == PauseButton || _previousButton == SettingsButton)
                {
                    _previousButton.Background = _sideDefault;
                }
                else
                {
                    _previousButton.Background = _navDefault;
                }
            }

            b.Background = _gazedUpon;
            _previousButton = b;

            switch (b.Name)
            {
                case "SlowForwardButton":
                    StateChange(ControlStates.SlowForward, SlowForwardPress);
                    break;
                case "FastForwardButton":
                    StateChange(ControlStates.FastForward, FastForwardPress);
                    break;
                case "SoftLeftButton":
                    StateChange(ControlStates.SoftLeft, SoftLeftPress);
                    break;
                case "SoftRightButton":
                    StateChange(ControlStates.SoftRight, SoftRightPress);
                    break;
                case "SharpLeftButton":
                    StateChange(ControlStates.SharpLeft, SharpLeftPress);
                    break;
                case "SharpRightButton":
                    StateChange(ControlStates.SharpRight, SharpRightPress);
                    break;
                case "StopButton":
                    StopButton.Background = _gazedUponStop;
                    StateChange(ControlStates.Stop, StopPress);
                    break;
                case "ReverseButton":
                    StateChange(ControlStates.Reverse, ReversePress);
                    break;
                case "DonutButton":
                    StateChange(ControlStates.Donut, DonutPress);
                    break;
                case "WaveButton":
                    StateChange(ControlStates.Wave, WavePress);
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
            if (!Globals._firstTimeHere)
            {
                Globals._gaze.GazePointerEvent += OnGazePointerEvent;
            }
            await InitializeCameraAsync();

            CreateBLEWatcher();
            _controlsEnabled = Globals._controlsEnabledPrev;
            Debug.WriteLine($"_controlsEnabled: {_controlsEnabled}");
            if (_controlsEnabled)
            {
                Connected();
            }
            base.OnNavigatedTo(e);
        }

        private void CreateBLEWatcher()
        {
            // Known Issues:
            //    - Navigating to SettingsPage before connecting to BLE will make the app non-responsive to the new connection
            //      that is attempted once returning to MainPage (happens after disconnecting from a previous connection, or before anything)
            //          > Under the hood, the Surface BLE does establish a connection but for some reason the high level UI doesnt respond
            //          > MainPage is being GarbageCollected during navigation to Settings...Collecting the BLE watcher stuff?
            //    - Navigating to SettingsPage after connecting to BLE will make the app non-responsive to the next time the BLE
            //      disconnects (Out of range, No power, etc)
            //          > LOW PRIORITY BUG because ExceptionHandlers will catch when users try and send commands, which will happen because the GUI
            //            does not indicate any disconnection, and this is handled by forcing the app into a DISCONNECTED state...then its good2go
            //          > HeartBeat can trigger this exception to be handled virtually immediately
            //
            //    + Possible TODOS:
            //      ==> Disconnect from BLE and dispose Watcher, then Create new Watcher
            //      ==> Prevent switching to Settings until the connection is established

            // Create a watcher to find a BLE Device with the correct UUID (or LocalName - commented out)
            Globals._bleAdv1 = new BluetoothLEAdvertisement();
            Globals._bleAdv1.ServiceUuids.Add(Globals._primaryServiceUUID);
            //Globals._bleAdv1.LocalName = "Pilot 37";
            Globals._bleAdvFilter1 = new BluetoothLEAdvertisementFilter();
            Globals._bleAdvFilter1.Advertisement = Globals._bleAdv1;
            Globals.bleWatch1 = new BluetoothLEAdvertisementWatcher(Globals._bleAdvFilter1);
            Globals.bleWatch1.ScanningMode = BluetoothLEScanningMode.Active;

            Globals.bleWatch1.Received += BLEWatcher_Received;
            Globals.bleWatch1.Stopped += BLEWatcher_Stopped;
            Globals.bleWatch1.Start();
            Debug.WriteLine("BLE Watcher has STARTED");
        }

        protected override void OnNavigatedFrom(NavigationEventArgs e)
        {
            Globals._gaze.GazePointerEvent -= OnGazePointerEvent;
            Globals._controlsEnabledPrev = _controlsEnabled;
            _controlsEnabled = false;
            //Globals.bleWatch1.Stop();
            base.OnNavigatedFrom(e);
        }

        private async void BLEWatcher_Received(BluetoothLEAdvertisementWatcher sender, BluetoothLEAdvertisementReceivedEventArgs args)
        {
            Globals._nordic = await BluetoothLEDevice.FromBluetoothAddressAsync(args.BluetoothAddress);
            if (Globals._nordic == null)
            {
                Debug.WriteLine("Error: BLE Device can't connect!");
                return;
            }

            Globals._nordic.ConnectionStatusChanged += NordicDevice_ConnectionChange;
            var _nordicServices = await Globals._nordic.GetGattServicesAsync();
            var services = _nordicServices.Services;

            if (services == null)
            {
                Debug.WriteLine("Error: Services from BLE Device can not be ascertained!");
                return;
            }

            foreach (GattDeviceService service in services)
            {
                if (service.Uuid.Equals(Globals._primaryServiceUUID))
                {
                    Globals._primaryService = service;
                }
                var _nordicChars = await service.GetCharacteristicsAsync();
                // Exception: Occured when BLE was still connected to previous session, then began a new session, and power cycled the BLE
                //            Happens when the BLE device is manually power-cycled too quickly.
                // System.IO.FileNotFoundException: 'The system cannot find the file specified. (Exception from HRESULT: 0x80070002)'
                var characteristics = _nordicChars.Characteristics;

                foreach (GattCharacteristic character in characteristics)
                {
                    SetEachCharacteristic(character);
                }
            }

            // Indicate that the device is now connected
            Debug.WriteLine("BLE Connection Status: CONNECTED");
            await CoreApplication.MainView.CoreWindow.Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () => { Connecting(); });

            // 2 Second Delay to allow for device connection
            _ticks = Environment.TickCount;
            while (Environment.TickCount - _ticks < 2000) ;

            await CoreApplication.MainView.CoreWindow.Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () => { Connected(); });
            _controlsEnabled = true;
            // Uncomment below to Enable Heart
            //_alive = new System.Threading.Timer(state => { keepAlive(); }, null, 0, _alivePeriod);
        }

        private void Connecting()
        {
            ConnectionStatus.Foreground = new SolidColorBrush(Colors.Yellow);
            ConnectionStatus.Text = "Connecting";
        }

        private void Connected()
        {
            ConnectionStatus.Foreground = new SolidColorBrush(Colors.Green);
            ConnectionStatus.Text = "Connected";
        }

        private void NotConnected()
        {
            ConnectionStatus.Foreground = new SolidColorBrush(Colors.Red);
            ConnectionStatus.Text = "Not Connected";
        }

        private void SetEachCharacteristic(GattCharacteristic c)
        {
            if (c.Uuid.Equals(Globals._heartBeatGUID))
            {
                Globals._heartBeatCharacteristic = c;
            }

            if (c.Uuid.Equals(Globals._GPIOGUID))
            {
                Globals._GPIOCharacteristic = c;
            }

            if (c.Uuid.Equals(Globals._PWMGUID))
            {
                Globals._PWMCharacteristic = c;
            }
        }

        private void BLEWatcher_Stopped(BluetoothLEAdvertisementWatcher sender, BluetoothLEAdvertisementWatcherStoppedEventArgs args)
        {
            Debug.WriteLine("BLE Watcher has STOPPED");
        }


        // Invoked every time the BLE device connects or disconnects
        private async void NordicDevice_ConnectionChange(BluetoothLEDevice sender, object args)
        {
            Debug.WriteLine("Connection Status Event!");
            Debug.WriteLine($"_controlsEnabled = {_controlsEnabled}");
            if (_controlsEnabled)
            {
                Debug.WriteLine("BLE Connection Status: DISCONNECTED");
                await CoreApplication.MainView.CoreWindow.Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () => { NotConnected(); });
                _controlsEnabled = false;
                await CoreApplication.MainView.CoreWindow.Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () => { Stop(); });
            }
        }

        private async void keepAlive()
        {
            if (_controlsEnabled)
            {
                await Globals._heartBeatCharacteristic.WriteValueAsync((new byte[] { 1 }).AsBuffer());
            }
        }

        private async Task InitializeCameraAsync()
        {
            if (_mediacCapture1 == null)
            {
                DeviceInformationCollection cameraDevices = await DeviceInformation.FindAllAsync(DeviceClass.VideoCapture);
                _fpvDevice1 = cameraDevices.First();
                
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

        private void Stop()
        {
            if (_previousButton != PauseButton)
            {
                _previousButton.Background = _navDefault;
            }
            _previousButton = StopButton;
            StopButton.Background = _gazedUponStop;
            StateChange(ControlStates.Stop, StopPress);
        }

        #region Button Callbacks
        private void Button_Click(object sender, RoutedEventArgs e)
        {
            var _button = sender as Button;
            if (_button != null)
            {
                switch (_button.Name)
                {
                    case "PauseButton":
                        PausePress();
                        return;
                    case "SettingsButton":
                        SettingsPress();
                        return;
                    default:
                        Button_Handler(_button);
                        break;
                }
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
            Globals.RECENT_SPEED_1 = Globals.SLOW_SPEED_1;
            Globals.RECENT_SPEED_2 = Globals.SLOW_SPEED_2;
            await Globals._PWMCharacteristic.WriteValueAsync((new byte[] { Globals.NEUTRAL_1, Globals.NEUTRAL_2, Globals.SLOW_SPEED_1, Globals.SLOW_SPEED_2 }).AsBuffer());
            //_control = new System.Threading.Timer(state => { SendBLEControl(someForwardArg); }, null, 0, _controlPeriod);
        }

        private async void FastForwardPress()
        {
            Debug.WriteLine("Fast Forward Press\n");
            //Debug.WriteLine($"Byte 1: {Globals.FAST_SPEED_1:X2}, Byte 2: {Globals.FAST_SPEED_2:X2}");
            Globals.RECENT_SPEED_1 = Globals.FAST_SPEED_1;
            Globals.RECENT_SPEED_2 = Globals.FAST_SPEED_2;
            await Globals._PWMCharacteristic.WriteValueAsync((new byte[] { Globals.NEUTRAL_1, Globals.NEUTRAL_2, Globals.FAST_SPEED_1, Globals.FAST_SPEED_2 }).AsBuffer());
            //_control = new System.Threading.Timer(state => { SendBLEControl(someForwardArg); }, null, 0, _controlPeriod);
        }

        private async void SharpRightPress()
        {
            Debug.WriteLine("Slow Sharp Right Press\n");
            Globals.RECENT_SPEED_1 = Globals.SLOW_SPEED_1;
            Globals.RECENT_SPEED_2 = Globals.SLOW_SPEED_2;
            await Globals._PWMCharacteristic.WriteValueAsync((new byte[] { Globals.SHARP_RIGHT_1, Globals.SHARP_RIGHT_2, Globals.SLOW_SPEED_1, Globals.SLOW_SPEED_2 }).AsBuffer());
            //_control = new System.Threading.Timer(state => { SendBLEControl(someForwardLeftArg); }, null, 0, _controlPeriod);
        }
        private async void SoftRightPress()
        {
            Debug.WriteLine("Slow Soft Right Press\n");
            await Globals._PWMCharacteristic.WriteValueAsync((new byte[] { Globals.SOFT_RIGHT_1, Globals.SOFT_RIGHT_2, Globals.RECENT_SPEED_1, Globals.RECENT_SPEED_2 }).AsBuffer());
            //_control = new System.Threading.Timer(state => { SendBLEControl(someForwardLeftArg); }, null, 0, _controlPeriod);
        }

        private async void SharpLeftPress()
        {
            Debug.WriteLine("Slow Sharp Left Press\n");
            Globals.RECENT_SPEED_1 = Globals.SLOW_SPEED_1;
            Globals.RECENT_SPEED_2 = Globals.SLOW_SPEED_2;
            await Globals._PWMCharacteristic.WriteValueAsync((new byte[] { Globals.SHARP_LEFT_1, Globals.SHARP_LEFT_2, Globals.SLOW_SPEED_1, Globals.SLOW_SPEED_2 }).AsBuffer());
            //_control = new System.Threading.Timer(state => { SendBLEControl(someForwardRightArg); }, null, 0, _controlPeriod);
        }

        private async void SoftLeftPress()
        {
            Debug.WriteLine("Slow Soft Left Press\n");
            await Globals._PWMCharacteristic.WriteValueAsync((new byte[] { Globals.SOFT_LEFT_1, Globals.SOFT_LEFT_2, Globals.RECENT_SPEED_1, Globals.RECENT_SPEED_2 }).AsBuffer());
            //_control = new System.Threading.Timer(state => { SendBLEControl(someForwardRightArg); }, null, 0, _controlPeriod);
        }

        private async void StopPress()
        {
            Debug.WriteLine("Stop Press\n");
            Globals.RECENT_SPEED_1 = Globals.SLOW_SPEED_1;
            Globals.RECENT_SPEED_2 = Globals.SLOW_SPEED_2;
            await Globals._PWMCharacteristic.WriteValueAsync((new byte[] { Globals.NEUTRAL_1, Globals.NEUTRAL_2, Globals.NEUTRAL_1, Globals.NEUTRAL_2 }).AsBuffer());
            //_control = new System.Threading.Timer(state => { SendBLEControl(someStopArg); }, null, 0, _controlPeriod);
        }

        private async void ReversePress()
        {
            Debug.WriteLine("Reverse Press\n");
            Globals.RECENT_SPEED_1 = Globals.SLOW_SPEED_1;
            Globals.RECENT_SPEED_2 = Globals.SLOW_SPEED_2;
            await Globals._PWMCharacteristic.WriteValueAsync((new byte[] { Globals.NEUTRAL_1, Globals.NEUTRAL_2, Globals.REVERSE_SPEED_1, Globals.REVERSE_SPEED_2 }).AsBuffer());
            //_control = new System.Threading.Timer(state => { SendBLEControl(someForwardRightArg); }, null, 0, _controlPeriod);
        }

        private async void DonutPress()
        {
            Debug.WriteLine("Donut Press\n");
            await Globals._PWMCharacteristic.WriteValueAsync((new byte[] { 0xC0, 0x07, 0xC0, 0x07 }).AsBuffer());
            //_control = new System.Threading.Timer(state => { SendBLEControl(someDonutArg); }, null, 0, _controlPeriod);
        }

        private async void WavePress()
        {
            Debug.WriteLine("Wave Press\n");
            await Globals._PWMCharacteristic.WriteValueAsync((new byte[] { 0xC0, 0x07, Globals.NEUTRAL_1, Globals.NEUTRAL_2 }).AsBuffer());
            // Brief Delay
            _ticks = Environment.TickCount;
            while (Environment.TickCount - _ticks < 100) ;
            await Globals._PWMCharacteristic.WriteValueAsync((new byte[] { 0xF8, 0x03, Globals.NEUTRAL_1, Globals.NEUTRAL_2 }).AsBuffer());
            // Brief Delay
            _ticks = Environment.TickCount;
            while (Environment.TickCount - _ticks < 100) ;
            await Globals._PWMCharacteristic.WriteValueAsync((new byte[] { 0xC0, 0x07, Globals.NEUTRAL_1, Globals.NEUTRAL_2 }).AsBuffer());
            // Brief Delay
            _ticks = Environment.TickCount;
            while (Environment.TickCount - _ticks < 100) ;
            await Globals._PWMCharacteristic.WriteValueAsync((new byte[] { 0xF8, 0x03, Globals.NEUTRAL_1, Globals.NEUTRAL_2 }).AsBuffer());
            // Brief Delay
            _ticks = Environment.TickCount;
            while (Environment.TickCount - _ticks < 100) ;
            await Globals._PWMCharacteristic.WriteValueAsync((new byte[] { Globals.NEUTRAL_1, Globals.NEUTRAL_2, Globals.NEUTRAL_1, Globals.NEUTRAL_2 }).AsBuffer());
            //_control = new System.Threading.Timer(state => { SendBLEControl(someDonutArg); }, null, 0, _controlPeriod);
        }

        private async void PausePress()
        {
            Debug.WriteLine("Pause Press\n");
            Globals.RECENT_SPEED_1 = Globals.SLOW_SPEED_1;
            Globals.RECENT_SPEED_2 = Globals.SLOW_SPEED_2;
            if (_controlsEnabled)
            {
                // Send STOP controls
                await Globals._PWMCharacteristic.WriteValueAsync((new byte[] { Globals.NEUTRAL_1, Globals.NEUTRAL_2, Globals.NEUTRAL_1, Globals.NEUTRAL_2 }).AsBuffer());
                //_control = new System.Threading.Timer(state => { SendBLEControl(someStopArg); }, null, 0, _controlPeriod);
            }

            _paused = !_paused;

            if (_previousButton != null && _previousButton != PauseButton)
            {
                _previousButton.Background = _navDefault;
            }

            StopButton.Background = _gazedUponStop;

            if (_paused)
            {
                PauseButton.Background = _gazedUpon;
                _previousButton = PauseButton;
            }
            else
            {
                PauseButton.Background = _sideDefault;
                _previousButton = StopButton;
                StateChange(ControlStates.Stop, StopPress);
            }
        }

        private void SettingsPress()
        {
            Debug.WriteLine("Settings Press\n");
            Globals._firstTimeHere = false;
            if (_previousButton != null && _previousButton != PauseButton)
            {
                _previousButton.Background = _navDefault;
            }

            Stop();

            Frame.Navigate(typeof(SettingsPage));

        }
        #endregion
    }
}
