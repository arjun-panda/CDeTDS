Place these files here for a branded Inno Setup installer:

  tds_icon.ico       — 256x256 app icon (multi-size .ico file)
                       Used for: installer EXE, desktop shortcut, Add/Remove Programs
  
  wizard_side.bmp    — 164 x 314 pixels, 24-bit BMP
                       Appears on the left side of the installer wizard
                       Tip: dark blue background, CDeTDS logo in white

  wizard_small.bmp   — 55 x 55 pixels, 24-bit BMP
                       Small header image on inner pages

Without these files:
  Comment out or remove these 3 lines in CDeTDS_Installer.iss [Setup]:
    SetupIconFile            = assets\tds_icon.ico
    WizardSmallImageFile     = assets\wizard_small.bmp
    WizardImageFile          = assets\wizard_side.bmp

Free icon resources:
  https://www.iconfinder.com
  https://icon-icons.com
  Create .ico from PNG: https://convertico.com
