# Pilot37
A platform to drive FPV Cars &amp; Drones from a Microsoft Surface using Eye Control

## Hardware & Software Requirements
To complete this project, you will need:
 * Tobii Bundle Version (???) and Drivers (???)
 * Tobii PCEye Mini sensor
 * Windows Surface or PC running Windows 10 Creators Update, Build Version 15063 or higher
 * USB 3.0 Hub
 * USB 5.8GHz Video Receiver (https://www.aliexpress.com/item/5-8G-FPV-Receiver-UVC-Video-Downlink-OTG-VR-Android-Phone-PC-Micro-USB-AMP-Pix/32723892492.html)
 * FOXEER TM600 FPV 5.8GHz 40CH 600mW Transmitter 
 * RunCam Eagle2 FPV Camera (http://shop.runcam.com/runcam-eagle-2/)
 * Traxxas Stampede RC Car
 * RedBear BLE Nano 2 (using Nordic nRF52832 chip)

## Set-Up
To get this project rolling, you will need to acquire one of the custom PCB breakout boards we designed to enable the Nano 2 to control the motors of the RC car/drone:
![alt text](https://github.com/TeamGleason/Pilot37/blob/master/photos/pcb.jpg)

You will need to solder the Nano 2 to this PCB, as well as some headers to expose the various PWM and GPIO ports of the board. With this done, the PCB can be placed onto the RC car/drone as follows (NOTE: This PCB is a copper-plated prototype made on an OtherMill):
![alt text](https://github.com/TeamGleason/Pilot37/blob/master/photos/pcb_in_use.jpg)

Once the PCB is ready and the headers are soldered, you can plug in your various servo motors and FPV transmitter according to the BLE specifications as to which pins output which signals.

To setup the remote controller portion of this project, you must first walk through the instructions given to you by the Tobii installation software to properly set up your eye-tracker sensor. Once finished, simply plug in your USB hub and attach both the PCEye Mini sensor and the USB 5.8GHz Video Receiver, and you are good to go. Now all you have to do is turn on the RC car/drone and launch the application.


## Things to Watch Out For
Sometimes the BLE connection can exhibit buggy behavior at the boundaries of the radio's range. Most of the time, bringing the BLE device back into the range of the Windows Surace or PC will solve all problems, but sometimes turning the device ON and OFF again will ensure proper behavior. If bugs persist, restart the application.

## Developers Note
For more robust and secure applications, consider enabling a Heartbeat and/or Watchdog timer to detect when the system is non-responsive. The current code does have a Heartbeat system in place (currently commented out), but it has not been tested and will undoubtedly be buggy.
There are only 2 files that make up this application, SettingsPage.xaml.cs and MainPage.xaml.cs. A single Gaze Pointer is created, which traverses both pages upon in-app frame navigation, although this should be changed in future iterations to match the standard programming pattern of other Eye Gaze applications (ie, on navigation to any new page, a new Gaze Pointer object should be created, then killed when navigating to a new page).

### Main Page
The MainPage is where the user will actually drive the RC car/drone. The background of this page displays the incoming FPV video stream from the RC car/drone, with semi-opaque driving buttons overlayed on top. Toggling the "Pause" button allows users to disable all functionality of the drive buttons until the "Pause" button is toggled once more. Clicking the settings button (button with gear icon) navigates the app to the Settings Page. To drive the RC car in reverse, users MUST select the reverse button, then select the stop button, then select the reverse button once more. A BLE indicator at the bottom-left of the screen displays the connection status of the BLE device.

### Settings Page
The main purpose of this page is to allow users to configure their own customized settings that work with their specific RC car. Users can save these customized settings so that they persist every time you re-launch the application. Users can also enable "personality actions" from the Settings page, which consists of a button to automatically do 'Donuts' and a button to 'Wave' at friends. Lastly, users can terminate the application from this Settings page.
Note: The Settings Page is DISABLED when the BLE device is DISCONNECTED from the Windows Surface or PC to avoid connection issues when the user navigates to the Settings Page. In this scenario, the Settings Button is converted into an "EXIT" button, which will terminate the app.
