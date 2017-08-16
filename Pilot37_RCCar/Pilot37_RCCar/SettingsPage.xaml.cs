using System;
using System.Diagnostics;
using Windows.UI;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Navigation;
using GazeInput;


namespace Pilot37_RCCar
{
    /// <summary>
    /// Settings Page for the Pilot37 RC Car application
    /// </summary>
    public sealed partial class SettingsPage : Page
    {

        private Button _previousSelect = null;
        private Button _previousValue = null;
        private TextBlock _currentSetting = null;
        private SolidColorBrush _selectDefault = new SolidColorBrush(Colors.Honeydew);
        private SolidColorBrush _selectActive = new SolidColorBrush(Colors.GreenYellow);
        private SolidColorBrush _valueDefault = new SolidColorBrush(Colors.CadetBlue);
        private SolidColorBrush _valueActive = new SolidColorBrush(Colors.RoyalBlue);
        private ControlStates _previousState;

        private enum ControlStates
        {
            FastForward,
            SlowForward,
            Reverse,
            SharpTurn,
            SoftTurn,
            Goodbye,
            Default
        }

        ControlStates _controlState = ControlStates.Default;

        public SettingsPage()
        {
            this.InitializeComponent();
            DataContext = this;
        }

        protected override void OnNavigatedTo(NavigationEventArgs e)
        {
            Globals._gaze.GazePointerEvent += OnGazePointerEvent;
            TextBlock_FastForwardValue.Text = Globals._fastForwardSetting;
            TextBlock_SlowForwardValue.Text = Globals._slowForwardSetting;
            TextBlock_ReverseValue.Text = Globals._reverseSetting;
            TextBlock_SharpTurnValue.Text = Globals._sharpTurnSetting;
            TextBlock_SoftTurnValue.Text = Globals._softTurnSetting;
            if (Globals._personality)
            {
                PersonalityButton.Background = new SolidColorBrush(Colors.DarkGreen);
            }
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
                        if (_button.Equals(ExitButton) || _button.Equals(SaveButton) || _button.Equals(CloseButton))
                        {
                            return;
                        }
                        Button_Handler(_button);
                        break;
                    case GazePointerState.Dwell:
                        switch (_button.Name)
                        {
                            case "ExitButton":
                                StateChange(ControlStates.Goodbye, ExitPress, _button);
                                break;
                            case "CloseButton":
                                StateChange(ControlStates.Goodbye, ClosePress, _button);
                                break;
                            case "SaveButton":
                                StateChange(ControlStates.Default, SavePress, _button);
                                break;
                        }
                        break;
                }
            }
        }

        private void Settings_Button_Click(object sender, RoutedEventArgs e)
        {
            var _button = sender as Button;
            if (_button != null)
            {
                switch (_button.Name)
                {
                    case "ExitButton":
                        StateChange(ControlStates.Goodbye, ExitPress, _button);
                        break;
                    case "CloseButton":
                        StateChange(ControlStates.Goodbye, ClosePress, _button);
                        break;
                    case "SaveButton":
                        StateChange(ControlStates.Default, SavePress, _button);
                        break;
                    default:
                        // Handling all the buttons that are not Dwell-activated
                        Button_Handler(_button);
                        break;
                }
            }
        }

        private void Button_Handler(Button b)
        {
            switch (b.Content)
            {
                case "SELECT":
                    switch (b.Name)
                    {
                        case "Button_SelectFastForward":
                            _currentSetting = TextBlock_FastForwardValue;
                            StateChange(ControlStates.FastForward, Select, b);
                            break;
                        case "Button_SelectSlowForward":
                            _currentSetting = TextBlock_SlowForwardValue;
                            StateChange(ControlStates.SlowForward, Select, b);
                            break;
                        case "Button_SelectReverse":
                            _currentSetting = TextBlock_ReverseValue;
                            StateChange(ControlStates.Reverse, Select, b);
                            break;
                        case "Button_SelectSharpTurn":
                            _currentSetting = TextBlock_SharpTurnValue;
                            StateChange(ControlStates.SharpTurn, Select, b);
                            break;
                        case "Button_SelectSoftTurn":
                            _currentSetting = TextBlock_SoftTurnValue;
                            StateChange(ControlStates.SoftTurn, Select, b);
                            break;
                    }
                    _previousSelect = b;
                    break;
                case "Personality":
                    PersonailtyPress();
                    break;
                default:
                    ValuePress(b);
                    break;
            }
        }

        private void StateChange(ControlStates state, Action<Button> func, Button b)
        {
            if (_controlState != state)
            {
                _previousState = _controlState;
                _controlState = state;
                func(b);
            }
        }

        private void SavePress(Button b)
        {
            Globals._localSettings.Values["fastForwardSetting"] = Globals._fastForwardSetting;
            Globals._localSettings.Values["slowForwardSetting"] = Globals._slowForwardSetting;
            Globals._localSettings.Values["reverseSetting"] = Globals._reverseSetting;
            Globals._localSettings.Values["sharpTurnSetting"] = Globals._sharpTurnSetting;
            Globals._localSettings.Values["softTurnSetting"] = Globals._softTurnSetting;

            Globals._localSettings.Values["fastForwardValue1"] = Convert.ToString(Globals.FAST_SPEED_1);
            Globals._localSettings.Values["fastForwardValue2"] = Convert.ToString(Globals.FAST_SPEED_2);
            Globals._localSettings.Values["slowForwardValue1"] = Convert.ToString(Globals.SLOW_SPEED_1);
            Globals._localSettings.Values["slowForwardValue2"] = Convert.ToString(Globals.SLOW_SPEED_2);
            Globals._localSettings.Values["reverseValue1"] = Convert.ToString(Globals.REVERSE_SPEED_1);
            Globals._localSettings.Values["reverseValue2"] = Convert.ToString(Globals.REVERSE_SPEED_2);
            Globals._localSettings.Values["sharpRightValue1"] = Convert.ToString(Globals.SHARP_RIGHT_1);
            Globals._localSettings.Values["sharpRightValue2"] = Convert.ToString(Globals.SHARP_RIGHT_2);
            Globals._localSettings.Values["sharpLeftValue1"] = Convert.ToString(Globals.SHARP_LEFT_1);
            Globals._localSettings.Values["sharpLeftValue2"] = Convert.ToString(Globals.SHARP_LEFT_2);
            Globals._localSettings.Values["softRightValue1"] = Convert.ToString(Globals.SOFT_RIGHT_1);
            Globals._localSettings.Values["softRightValue2"] = Convert.ToString(Globals.SOFT_RIGHT_2);
            Globals._localSettings.Values["softLeftValue1"] = Convert.ToString(Globals.SOFT_LEFT_1);
            Globals._localSettings.Values["softLeftValue2"] = Convert.ToString(Globals.SOFT_LEFT_2);
            
            FadeStoryboard.Begin();
        }

        private void ClosePress(Button b)
        {
            this.Frame.Navigate(typeof(MainPage));
        }

        private void ExitPress(Button b)
        {
            DisconnectFromBLE();
            Application.Current.Exit();
        }

        private void PersonailtyPress()
        {
            if (Globals._personality)
            {
                PersonalityButton.Background = new SolidColorBrush(Colors.DarkRed);
            } else
            {
                PersonalityButton.Background = new SolidColorBrush(Colors.DarkGreen);
            }

            Globals._personality = !Globals._personality;
        }

        private void DisconnectFromBLE()
        {
            Globals._bleAdv1 = null;
            Globals._bleAdvFilter1 = null;

            if (Globals.bleWatch1 != null)
            {
                Globals.bleWatch1.Stop();
                Globals.bleWatch1 = null;
            }

            // Dispose all BLE related variables
            //if (_alive != null)
            //{
            //    _alive.Dispose();
            //    _alive = null;
            //}

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

        private void Select(Button b)
        {
            if (_previousSelect != null)
            {
                _previousSelect.Background = _selectDefault;
            }
            if (_previousValue != null)
            {
                _previousValue.Background = _valueDefault;
            }
            b.Background = _selectActive;
        }

        private void ValuePress(Button b)
        {
            String _content = b.Content as String;

            if (_previousValue != null)
            {
                _previousValue.Background = _valueDefault;
            }
            b.Background = _valueActive;
            if (_currentSetting != null)
            {
                _currentSetting.Text = _content;
            }
            
            switch (_controlState)
            {
                case ControlStates.FastForward:
                    Globals._fastForwardSetting = _content;
                    Byte[] _fastSpeeds = ConvertToControlBytes(_content, 500, true);
                    //Debug.WriteLine($"Byte 1: {_fastSpeeds[0]:X2}, Byte 2: {_fastSpeeds[1]:X2}");
                    Globals.FAST_SPEED_1 = _fastSpeeds[0];
                    Globals.FAST_SPEED_2 = _fastSpeeds[1];
                    break;
                case ControlStates.SlowForward:
                    Globals._slowForwardSetting = _content;
                    Byte[] _slowSpeeds = ConvertToControlBytes(_content, 50, true);
                    //Debug.WriteLine($"Byte 1: {_slowSpeeds[0]:X2}, Byte 2: {_slowSpeeds[1]:X2}");
                    Globals.SLOW_SPEED_1 = _slowSpeeds[0];
                    Globals.SLOW_SPEED_2 = _slowSpeeds[1];
                    break;
                case ControlStates.Reverse:
                    Globals._reverseSetting = _content;
                    Byte[] _reverseSpeeds = ConvertToControlBytes(_content, 500, false);
                    //Debug.WriteLine($"Byte 1: {_reverseSpeeds[0]:X2}, Byte 2: {_reverseSpeeds[1]:X2}");
                    Globals.REVERSE_SPEED_1 = _reverseSpeeds[0];
                    Globals.REVERSE_SPEED_2 = _reverseSpeeds[1];
                    break;
                case ControlStates.SharpTurn:
                    Globals._sharpTurnSetting = _content;
                    Byte[] _sharpLeftSpeeds = ConvertToControlBytes(_content, 500, true);
                    //Debug.WriteLine($"Left Byte 1: {_sharpLeftSpeeds[0]:X2}, Left Byte 2: {_sharpLeftSpeeds[1]:X2}");
                    Globals.SHARP_LEFT_1 = _sharpLeftSpeeds[0];
                    Globals.SHARP_LEFT_2 = _sharpLeftSpeeds[1];

                    Byte[] _sharpRightSpeeds = ConvertToControlBytes(_content, 500, false);
                    //Debug.WriteLine($"Right Byte 1: {_sharpRightSpeeds[0]:X2}, Right Byte 2: {_sharpRightSpeeds[1]:X2}");
                    Globals.SHARP_RIGHT_1 = _sharpRightSpeeds[0];
                    Globals.SHARP_RIGHT_2 = _sharpRightSpeeds[1];
                    break;
                case ControlStates.SoftTurn:
                    Globals._softTurnSetting = _content;
                    Byte[] _softLeftSpeeds = ConvertToControlBytes(_content, 250, true);
                    //Debug.WriteLine($"Left Byte 1: {_softLeftSpeeds[0]:X2}, Left Byte 2: {_softLeftSpeeds[1]:X2}");
                    Globals.SOFT_LEFT_1 = _softLeftSpeeds[0];
                    Globals.SOFT_LEFT_2 = _softLeftSpeeds[1];

                    //Debug.WriteLine($"Right Byte 1: {_softRightSpeeds[0]:X2}, Right Byte 2: {_softRightSpeeds[1]:X2}");
                    Byte[] _softRightSpeeds = ConvertToControlBytes(_content, 250, false);
                    Globals.SOFT_RIGHT_1 = _softRightSpeeds[0];
                    Globals.SOFT_RIGHT_2 = _softRightSpeeds[1];
                    break;
            }

            _previousValue = b;
        }

        private byte[] ConvertToControlBytes(String percentage, Double range, bool pos)
        {
            Char _delimiter = '%';
            Double x = Double.Parse(percentage.Split(_delimiter)[0]);
            x = (x / 100) * range;

            if (pos)
            {
                if (range == 50)
                {
                    x = x + 1560;
                } else
                {
                    x = x + 1500;
                }
            } else
            {
                x = 1500 - x;
                if (x > 1420)
                {
                    x = 1420;
                }
            }

            UInt16 _val = Convert.ToUInt16(x);
            return BitConverter.GetBytes(_val);
        }

    }
}
