using System;
using System.IO;
using System.Net.Sockets;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Linq;
using Microsoft.UI.Dispatching;
using KonnectChatIRC.Models;

namespace KonnectChatIRC.Services
{
    public class IrcMessageEventArgs : EventArgs
    {
        public string RawMessage { get; }
        public string Prefix { get; }
        public string Command { get; }
        public string[] Parameters { get; }

        public IrcMessageEventArgs(string raw, string prefix, string command, string[] parameters)
        {
            RawMessage = raw;
            Prefix = prefix;
            Command = command;
            Parameters = parameters;
        }
    }

    public class IrcClientService
    {
        private TcpClient? _tcpClient;
        private StreamReader? _reader;
        private StreamWriter? _writer;
        private bool _isConnected;
        private readonly DispatcherQueue _dispatcherQueue;

        public event EventHandler<IrcMessageEventArgs>? MessageReceived;
        public event EventHandler? Connected;
        public event EventHandler? WelcomeReceived; // New event for Auto-join timing
        public event EventHandler<IrcChannelInfo>? ChannelFound;
        public event EventHandler? Disconnected;
        public event EventHandler<string>? ErrorOccurred;

        public IrcClientService()
        {
            _dispatcherQueue = DispatcherQueue.GetForCurrentThread();
        }

        public async Task ConnectAsync(string server, int port, string nickname, string realname, string? password = null)
        {
            try
            {
                _tcpClient = new TcpClient();
                await _tcpClient.ConnectAsync(server, port);
                var stream = _tcpClient.GetStream();
                _reader = new StreamReader(stream);
                _writer = new StreamWriter(stream) { AutoFlush = true };

                _isConnected = true;
                
                // Start reading loop
                _ = ReadLoop();

                // Send login commands
                if (!string.IsNullOrEmpty(password))
                {
                    await SendRawAsync($"PASS {password}");
                }
                await SendRawAsync($"NICK {nickname}");
                await SendRawAsync($"USER {nickname} 0 * :{realname}");

                _dispatcherQueue.TryEnqueue(() => Connected?.Invoke(this, EventArgs.Empty));
            }
            catch (Exception ex)
            {
                _dispatcherQueue.TryEnqueue(() => ErrorOccurred?.Invoke(this, $"Connection failed: {ex.Message}"));
            }
        }

        public async Task SendRawAsync(string message)
        {
            if (_isConnected && _writer != null)
            {
                await _writer.WriteLineAsync(message);
            }
        }

        public async Task JoinChannelAsync(string channel)
        {
           await SendRawAsync($"JOIN {channel}");
        }

        public async Task SendMessageAsync(string target, string message)
        {
            await SendRawAsync($"PRIVMSG {target} :{message}");
        }

        private async Task ReadLoop()
        {
            try
            {
                while (_isConnected && _tcpClient != null && _tcpClient.Connected && _reader != null)
                {
                    string? line = await _reader.ReadLineAsync();
                    if (line == null) break;

                    ParseAndHandle(line);
                }
            }
            catch (Exception ex) when (_isConnected)
            {
                 // Only report if we didn't initiate the disconnect
                 // If we sent QUIT, the server might close the connection, causing an IOException here.
                 // We can check if we are still "connected" logic-wise.
                 
                 _dispatcherQueue.TryEnqueue(() => ErrorOccurred?.Invoke(this, $"Read error: {ex.Message}"));
            }
            finally
            {
                // Ensure we clean up, but don't overwrite a custom quit message if one was already sent.
                // If we are here due to an error, we can disconnect silently or with a default.
                Disconnect(null);
            }
        }

        // State for accumulating Whois data
        private WhoisInfo? _currentWhois;

        public event EventHandler<WhoisInfo>? WhoisReceived;

        private void ParseAndHandle(string line)
        {
            // Simple IRC Parser
            // Format: [:prefix] command [params] [:trailing]
            
            string prefix = "";
            string command = "";
            var parameters = new List<string>();
            string trailing = "";

            string remaining = line;

            if (remaining.StartsWith(":"))
            {
                int prefixEnd = remaining.IndexOf(' ');
                if (prefixEnd != -1)
                {
                    prefix = remaining.Substring(1, prefixEnd - 1);
                    remaining = remaining.Substring(prefixEnd + 1);
                }
            }

            int trailingStart = remaining.IndexOf(" :");
            if (trailingStart != -1)
            {
                trailing = remaining.Substring(trailingStart + 2);
                remaining = remaining.Substring(0, trailingStart);
            }

            var parts = remaining.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length > 0)
            {
                command = parts[0];
                for (int i = 1; i < parts.Length; i++)
                {
                    parameters.Add(parts[i]);
                }
            }
            
            if (!string.IsNullOrEmpty(trailing))
            {
                parameters.Add(trailing);
            }

            // Handle PING immediately
            if (command == "PING")
            {
                string token = parameters.Count > 0 ? parameters[0] : "";
                _ = SendRawAsync($"PONG :{token}");
                return; // Don't necessarily need to bubble this up
            }
            
            if (command == "001") // RPL_WELCOME
            {
                 _dispatcherQueue.TryEnqueue(() => WelcomeReceived?.Invoke(this, EventArgs.Empty));
            }

            // Handle RPL_LIST (322)
            if (command == "322") 
            {
                 // 322 Nick #channel 5 :topic
                 if (parameters.Count >= 3)
                 {
                     // Try standard index 1 for channel
                     string charName = parameters[1];
                     int userCountVal = 0;
                     string topicVal = "";

                     // If param 2 is integer, that matches standard
                     if (int.TryParse(parameters[2], out userCountVal))
                     {
                         if(parameters.Count > 3) topicVal = parameters[3];
                     }
                     else if (parameters.Count > 3 && int.TryParse(parameters[3], out userCountVal))
                     {
                         // Sometimes there might be extra params? Fallback logic
                         charName = parameters[2];
                         if (parameters.Count > 4) topicVal = parameters[4];
                     }

                     if (charName.StartsWith("#") || charName.StartsWith("&"))
                     {
                         ChannelFound?.Invoke(this, new IrcChannelInfo { Name = charName, UserCount = userCountVal, Topic = topicVal });
                     }
                 }
            }

            // Handle Whois
            HandleWhois(command, parameters);

            // Dispatch event
            _dispatcherQueue.TryEnqueue(() => 
            {
                MessageReceived?.Invoke(this, new IrcMessageEventArgs(line, prefix, command, parameters.ToArray()));
            });
        }

        private void HandleWhois(string command, List<string> parameters)
        {
            // 311 RPL_WHOISUSER: <nick> <user> <host> * :<real name>
            if (command == "311" && parameters.Count >= 6) 
            {
                _currentWhois = new WhoisInfo

                {
                    Nickname = parameters[1],
                    Username = parameters[2],
                    Hostname = parameters[3],
                    Realname = parameters[5]
                };
            }
            // 312 RPL_WHOISSERVER: <nick> <server> :<server info>
            else if (command == "312" && _currentWhois != null && parameters.Count >= 3)
            {
                _currentWhois.Server = parameters[2];
                if (parameters.Count > 3) _currentWhois.ServerInfo = parameters[3];
            }
            // 378 RPL_WHOISHOST: <nick> <target> :is connecting from <user>@<host> <ip>
            else if (command == "378" && _currentWhois != null && parameters.Count >= 3)
            {
                string hostInfo = parameters[2];
                if (hostInfo.StartsWith("is connecting from ", StringComparison.OrdinalIgnoreCase))
                {
                    hostInfo = hostInfo.Substring("is connecting from ".Length);
                }
                _currentWhois.ConnectingFrom = hostInfo;
            }
            // 319 RPL_WHOISCHANNELS: <nick> :<channel> <channel> ...
            else if (command == "319" && _currentWhois != null && parameters.Count >= 3)
            {
                string channelList = parameters.Last(); // Trailing param has the list
                var channels = channelList.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                _currentWhois.Channels.AddRange(channels);
            }
            // 313 RPL_WHOISOPERATOR: <nick> :is an IRC operator
            else if (command == "313" && _currentWhois != null)
            {
                _currentWhois.IsOperator = true;
            }
            // 317 RPL_WHOISIDLE: <nick> <idle_seconds> <signon_unix> :seconds idle, signon time
            else if (command == "317" && _currentWhois != null && parameters.Count >= 4)
            {
                if (int.TryParse(parameters[2], out int idleSecs))
                {
                    _currentWhois.IdleSeconds = idleSecs;
                }
                if (long.TryParse(parameters[3], out long signonUnix))
                {
                    _currentWhois.SignonTime = DateTimeOffset.FromUnixTimeSeconds(signonUnix).UtcDateTime;
                }
            }
            // 301 RPL_AWAY (in WHOIS context): <nick> <target> :away message
            else if (command == "301" && _currentWhois != null && parameters.Count >= 3)
            {
                _currentWhois.IsAway = true;
                _currentWhois.AwayMessage = parameters[2];
            }
            // 318 RPL_ENDOFWHOIS: <nick> :End of WHOIS list
            else if (command == "318" && _currentWhois != null)
            {
                var info = _currentWhois;
                _currentWhois = null; // Reset
                _dispatcherQueue.TryEnqueue(() => WhoisReceived?.Invoke(this, info));
            }
        }
        
        public async Task ListChannelsAsync()
        {
            await SendRawAsync("LIST");
        }


        public void Disconnect(string? quitMessage = null)
        {
            if (!_isConnected) return;

            try
            {
                if (!string.IsNullOrEmpty(quitMessage) && _writer != null)
                {
                    _writer.WriteLine($"QUIT :{quitMessage}");
                    _writer.Flush();
                }
            }
            catch { }

            _isConnected = false;
            try { _writer?.Dispose(); } catch { }
            try { _reader?.Dispose(); } catch { }
            try { _tcpClient?.Close(); } catch { }

            _dispatcherQueue.TryEnqueue(() => Disconnected?.Invoke(this, EventArgs.Empty));
        }
    }
}
