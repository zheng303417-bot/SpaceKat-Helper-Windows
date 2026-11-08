# ViGEm client dependency

- Publisher package: Nefarius.ViGEm.Client 1.21.256, https://www.nuget.org/packages/Nefarius.ViGEm.Client/1.21.256
- Download: https://api.nuget.org/v3-flatcontainer/nefarius.vigem.client/1.21.256/nefarius.vigem.client.1.21.256.nupkg
- Package SHA-256: FF460031B33E0882CD1166FE2D072D0FE97578480C61ED85DCCFA6C930FDB55E
- Both native DLLs extracted without modification from the managed package assembly's embedded costura64.vigemclient.dll / costura32.vigemclient.dll resources. The managed assembly is not a runtime dependency.
- DLL file/product version: 1.21.233.0, publisher metadata Nefarius Software Solutions e.U.
- x64/ViGEmClient.dll SHA-256: 00303F99E1968D523FBD08A7C691D60D145B7335059EC593947841D124C10875
- x86/ViGEmClient.dll SHA-256: 5115F9A8B913B110D531A494387C6C7ED908DAFFF75995F1ED33BD892602753F
- Native SDK/API source: https://github.com/nefarius/ViGEmClient
- ABI reference: https://github.com/nefarius/ViGEm.NET/blob/master/ViGEmClient/ViGEmClient.Native.cs
- License source: https://github.com/nefarius/ViGEmClient/blob/master/LICENSE (included as LICENSE-ViGEmClient.txt).

Only the user-mode client is redistributed. This program does not install a driver. Creating a virtual controller requires an already working compatible ViGEmBus installation. The native client and driver are third-party components; availability is checked at runtime rather than inferred from a driver file's existence.
