# Support Fins – SolidWorks Add-in

Native C# SolidWorks Add-in embedding the Support Fins calculation engine via Jint (zero external runtime dependencies).

## Installation
1. Download the release build or compile the solution in Visual Studio (`x64 Debug` or `Release`).
2. Right-click `install.bat` and select **Run as Administrator** to register the COM Add-in.
3. Start SolidWorks and open **Tools -> Add-Ins** to verify that "Support Fins" is active.

## Usage
1. Open any Part document with a valid solid body.
2. Click **Support Fins** in the CommandManager ribbon.
3. The plugin generates the fins with a 0.2 mm air gap, tine contacts, and bed pads, importing them directly as solid bodies into your active part.
