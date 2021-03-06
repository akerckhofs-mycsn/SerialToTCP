using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System.IO.Ports;
using System.Net;
using System.Net.Sockets;
using System.Text;
using Polly;

namespace SerialToTCP
{
    public class Worker : BackgroundService
    {
        private readonly ILogger<Worker> _logger;
        private SerialPort serialPort;
        private readonly string portName;
        private readonly int tcpPort;
        private TcpListener tcpListener;
        private readonly List<TcpClient> tcpClients = new List<TcpClient>();

        public Worker(ILogger<Worker> logger) {
            _logger = logger;
            portName = Environment.GetEnvironmentVariable("S2TCP_SERIAL_PORT") ?? "COM4";
            tcpPort = int.Parse(Environment.GetEnvironmentVariable("S2TCP_TCP_PORT") ?? "6001");
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken) {
            InitializeSerialPort();
            InitializeTcpListener();

            while (!stoppingToken.IsCancellationRequested)
            {
                if (tcpListener!.Pending())
                {
                    var client = await tcpListener.AcceptTcpClientAsync();
                    tcpClients?.Add(client);
                    _logger.LogInformation("Accepted client at {ipAddress}",
                        (client?.Client?.RemoteEndPoint as IPEndPoint)?.Address);
                }

                await Task.Delay(100, stoppingToken);
            }
        }

        private void InitializeSerialPort() {
            Policy.Handle<IOException>()
                  .WaitAndRetryForever(attempt => TimeSpan.FromMilliseconds(Math.Pow(2, attempt) * 500))
                  ?.Execute(() => { serialPort = new SerialPort(portName, 9600); });

            serialPort!.DataReceived += SerialPortOnDataReceived;
            serialPort!.ErrorReceived += SerialPortOnErrorReceived;

            Policy.Handle<IOException>()
                  ?.Or<UnauthorizedAccessException>()
                  .WaitAndRetryForever(attempt => TimeSpan.FromMilliseconds(Math.Min(5000, Math.Pow(2, attempt) * 500)),
                      (exception, i, arg3) => { _logger.LogError(exception.Message); })
                  ?.Execute(serialPort.Open);

            _logger.LogInformation("Opened Serial Port..");
        }

        private void InitializeTcpListener() {
            tcpListener = new TcpListener(IPAddress.Any, tcpPort);

            Policy.Handle<SocketException>()
                  .WaitAndRetryForever(attempt =>
                          TimeSpan.FromMilliseconds(Math.Min(5000, Math.Pow(2, attempt) * 500)),
                      (exception, i, arg3) => {
                          _logger.LogError($"[{(exception as SocketException).ErrorCode}]{exception.Message}");
                      })
                  ?.Execute(tcpListener.Start);

            _logger.LogInformation("Started TCP Listener..");
        }

        private void SerialPortOnErrorReceived(object sender, SerialErrorReceivedEventArgs e) {
            var sp = (SerialPort) sender;
            if (sp == null)
            {
                Policy.Handle<IOException>()
                      .WaitAndRetryForever(attempt => TimeSpan.FromMilliseconds(Math.Pow(2, attempt) * 500),
                          (exception, i, arg3) => {
                              _logger.LogError("Failed to initialize serial port: {message}", exception?.Message);
                          })
                      ?.Execute(InitializeSerialPort);
            } else if (!sp.IsOpen)
            {
                Policy.Handle<IOException>()
                      ?.Or<UnauthorizedAccessException>()
                      .WaitAndRetryForever(attempt => TimeSpan.FromMilliseconds(Math.Pow(2, attempt) * 500),
                          (exception, i, arg3) => {
                              _logger.LogError("Failed to open serial port: {message}", exception?.Message);
                          })
                      ?.Execute(serialPort.Open);
            }
        }

        private void SerialPortOnDataReceived(object sender, SerialDataReceivedEventArgs e) {
            var sp = (SerialPort) sender;
            var data = sp?.ReadExisting();

            _logger.LogInformation("Received {data} from {port}", data, portName);

            tcpClients?.ForEach(client => {
                if (!client.Connected)
                {
                    client.Dispose();
                } else
                {
                    try
                    {
                        var stream = client.GetStream();
                        stream.Write(Encoding.UTF8.GetBytes(data));
                        stream.Flush();
                        _logger.LogDebug("Sending data to client at {ipAddress}",
                            (client.Client?.RemoteEndPoint as IPEndPoint)?.Address);
                    } catch (IOException exception)
                    {
                        _logger.LogError($"[TCP Server] {exception.Message}");
                    }
                }
            });
        }
    }
}