# Serial to TCP

## Build

```
dotnet publish -r win-x64 -c Release
```

## Installation

Open up a privileged terminal and run the following command:

```
sc.exe create SerialToTCP C:\full\path\to\executable\SerialToTCP.exe
```

Proceed to set the service to Automatic or Automatic (Delayed start) if it isn't considered critical.

## Information

The service will open the serial port specified by the environment variable `S2TCP_SERIAL_PORT`. The baudrate is set at 9600.
A TCP server will listen on the port specified by the environment variable `S2TCP_TCP_PORT`.

If one of these environment variables is unset, the service defaults to `COM4` and `6001`.