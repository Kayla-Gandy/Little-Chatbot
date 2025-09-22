using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

ChatBot.MessageProcessor messenger = new ChatBot.MessageProcessor();
messenger.StartReading();

namespace ChatBot {
    public enum ClaudeModels
    {
        ClaudeOpus41 = 0,
        ClaudeOpus4 = 1,
        ClaudeSonnet4 = 2,
        ClaudeSonnet37 = 3,
        ClaudeHaiku35 = 4,
        ClaudeHaiku3 = 5
    }

    public class Message {
        public string role { get; set; } = "";
        public string content { get; set; } = "";
    } // Message

    public class Content {
        public string type { get; set; } = "";
        public string text { get; set; } = "";
    } // Content

    public class MessageResponse {
        public string role { get; set; } = "";
        public List<Content> content { get; set; } = new List<Content>();
    } // MessageResponse

    public class ChatBotMessenger {

        private readonly Dictionary<ClaudeModels, string> _claudeModelStrings = new Dictionary<ClaudeModels, string> {
            { ClaudeModels.ClaudeOpus41, "claude-opus-4-1-20250805" },
            { ClaudeModels.ClaudeOpus4, "claude-opus-4-20250514" },
            { ClaudeModels.ClaudeSonnet4, "claude-sonnet-4-20250514" },
            { ClaudeModels.ClaudeSonnet37, "claude-3-7-sonnet-latest" },
            { ClaudeModels.ClaudeHaiku35, "claude-3-5-haiku-latest" },
            { ClaudeModels.ClaudeHaiku3, "claude-3-haiku-20240307" }
        };

        HttpClient _client = new HttpClient();

        private const string _endSessionCommand = "end session";

        private const string _apiKeyFile = "apiKey.txt";

        private const string _url = "https://api.anthropic.com/v1/messages";

        public Dictionary<string, string> _header { get; set; } = new Dictionary<string, string> {
            { "anthropic-version", "2023-06-01" }
        };

        public ClaudeModels _currentModel { get; set; } = ClaudeModels.ClaudeOpus41;

        public uint _maxTokens { get; set; } = 50; 

        public ChatBotMessenger()
        {
            try {
                string apiKey = File.ReadAllText(_apiKeyFile);
                if (!string.IsNullOrEmpty(apiKey)) {
                    string strippedApiKey = apiKey.Trim('\r', '\n');
                    _header.Add("x-api-key", strippedApiKey);
                } else
                    Console.Write($"Please add your API key to {_apiKeyFile}\n");
            } catch (Exception) {
                Console.WriteLine("Error Reading API key");
            }
        }

        public Message SendMessage(List<Message> messageContext)
        {
            HttpRequestMessage httpMessenger = new HttpRequestMessage(HttpMethod.Post, _url);
            foreach (KeyValuePair<string, string> entry in _header) {
                httpMessenger.Headers.Add(entry.Key, entry.Value);
            }

            var requestBody = new {
                model = _claudeModelStrings[_currentModel],
                max_tokens = _maxTokens,
                messages = messageContext
            };
            httpMessenger.Content = new StringContent(JsonSerializer.Serialize(requestBody), Encoding.UTF8, "application/json");
            HttpResponseMessage response = _client.Send(httpMessenger);
            if (!response.IsSuccessStatusCode) {
                int statusCodeInt = (int)response.StatusCode;
                Console.Write($"Error code from server: {statusCodeInt}. Could not get response.\n");
                Console.Write(response.Content.ReadAsStringAsync().Result ?? "");
                return new Message();
            }

            string responseBody = response.Content.ReadAsStringAsync().Result ?? "";
            MessageResponse responseObj = JsonSerializer.Deserialize<MessageResponse>(responseBody) ?? new MessageResponse();
            if (responseObj.content.Count() == 0) {
                Console.Write("Response content empty.\n");
                return new Message();
            }
            string contentText = responseObj.content[0].text;
            Message responseMessage = new Message{
                role = responseObj.role,
                content = contentText
            };

            return responseMessage;
        }

        public void StartMessenger(ChatLogger logger)
        {
            string currentMessage = "";
            while (currentMessage.ToLower() != _endSessionCommand) {
                currentMessage = Console.ReadLine() ?? "";
                if (string.IsNullOrEmpty(currentMessage) || currentMessage.ToLower() == _endSessionCommand)
                    continue;
                
                Message messageObj = new Message{
                    role = "user",
                    content = currentMessage
                };
                logger.AddMessageToContext(messageObj);
                Message responseMessage = SendMessage(logger.GetFullContext());
                if (!string.IsNullOrEmpty(responseMessage.content))
                    logger.AddMessageToContext(responseMessage);

                Console.Write("\nBot Response: ");
                Console.Write(responseMessage.content);
                Console.Write("\n");

            }
        }
    } // ChatBotMessenger

    public class ChatLogger {
        private const string _logDir = "ChatHistory";
        private string _currentLogPath = "";
        public List<Message> _currentContext = new List<Message>();

        public List<Message> GetFullContext()
        {
            return _currentContext;
        }

        public void ListLogs()
        {
            Console.Write("Session Names: \n");
            string[] files = Directory.GetFiles(_logDir);
            foreach (string filePath in files)
            {
                string filename = Path.GetFileName(filePath);
                Console.WriteLine(filename);
            }
            Console.Write("\n");
        }

        public bool AddMessageToContext(Message message)
        {
            _currentContext.Add(message);
            try {
                File.WriteAllText(_currentLogPath, JsonSerializer.Serialize(_currentContext));
            } catch (IOException) {
                return false;
            }
            return true;
        }

        public bool LoadContext(string logName)
        {
            string logPath = Path.Combine(_logDir, logName);
            if (!File.Exists(logPath))
                return false;
            try {
                string logText = File.ReadAllText(logPath) ?? "";
                if (string.IsNullOrEmpty(logText))
                    return false;
                _currentContext = JsonSerializer.Deserialize<List<Message>>(logText) ?? _currentContext;
                _currentLogPath = logPath;
            } catch (IOException) {
                return false;
            }

            Console.WriteLine($"Session '{logName}'\n");
            return true;
        }

        public bool CreateNewContext()
        {
            DateTime currentTime = DateTime.Now;
            string isoTimestamp = currentTime.ToString("yyyy-MM-dd'T'HH:mm:ss.fffK", System.Globalization.DateTimeFormatInfo.InvariantInfo);
            _currentLogPath = Path.Combine(_logDir, isoTimestamp);
            try {
                FileStream fs = File.Create(_currentLogPath); 
                fs.Close();
                Console.WriteLine($"Session {isoTimestamp}\n");
            } catch (Exception ex) {
                Console.WriteLine($"Error starting session: {ex.Message}");
                return false;
            }
            return true;
        }
    } // ChatLogger

    public class MessageProcessor {
        public void ProcessSessionLoad(ref ChatLogger logger)
        {
            bool sessionLoaded = false;
            while (!sessionLoaded) {
                Console.Write("Please enter session start date/time. Enter \"list sessions\" to list all saved chats.\n");
                string sessionName = Console.ReadLine() ?? "";
                if (sessionName.ToLower() == "list sessions") {
                    logger.ListLogs();
                    continue;
                }

                string strippedSessionName = sessionName.Trim('\r', '\n');
                bool loadSuccess = logger.LoadContext(sessionName);
                if (loadSuccess) {
                    sessionLoaded = true;
                    Console.Write("Start Chatting!\n");
                } else
                    Console.Write("Session could not be found.\n");
            }
        }

        public void ProcessNewSession(ref ChatLogger logger)
        {
            if (!logger.CreateNewContext())
                return;
            Console.Write("Start Chatting!\n");
        }

        public void StartReading()
        {
            bool quit = false;
            while (!quit) {
                ChatLogger logger = new ChatLogger();
                ChatBotMessenger messenger = new ChatBotMessenger();
                Console.Write("Load session or new session?\n");
                string choice = Console.ReadLine() ?? "";
                choice = choice.ToLower();
                switch (choice) {
                    case "load session":
                        ProcessSessionLoad(ref logger);
                        messenger.StartMessenger(logger);
                        break;
                    case "new session":
                        ProcessNewSession(ref logger);
                        messenger.StartMessenger(logger);
                        break;
                    case "list sessions":
                        logger.ListLogs();
                        break;
                    case "quit":
                        quit = true;
                        break;
                    default:
                        Console.Write("Command not recognized. Please enter \"load session\", \"new session\", \"list sessions\", or \"quit\".\n");
                        break;
                }
            }
        }
    } // MessageProcessor
} // ChatBot