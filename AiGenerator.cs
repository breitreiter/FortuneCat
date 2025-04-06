using OpenAI.Managers;
using OpenAI;
using OpenAI.ObjectModels.RequestModels;
using OpenAI.ObjectModels;
using Azure.Storage.Blobs;
using Pinecone;
using System.Diagnostics;

namespace FortuneService
{
    public class GeneratorSettings
    {
        public string? OpenAiKey;
        public string? AzureBlobKey;
        public string? AzureBlobContainer;
        public string? AzureBlobSceenStateFilename;
        public string? PineconeApiKey;
        public string? PineconeEnvironment;
        public string? PineconeIndex;
        public string? PineconeNamespace;

        public GeneratorSettings(IConfiguration config)
        {
            // This is namespaced in secrets.json, but not in Azure appsettings
            string ns = "FortuneCat:";

            OpenAiKey = config[ns + "OpenAiKey"];
            if (OpenAiKey == null) 
            {
                ns = string.Empty;
                OpenAiKey = config[ns + "OpenAiKey"];
            }
            AzureBlobKey = config[ns + "AzureBlobKey"]; 
            AzureBlobContainer = config[ns + "AzureBlobContainer"]; 
            AzureBlobSceenStateFilename = config[ns + "AzureBlobFilename"]; 
            PineconeApiKey = config[ns + "PineconeApiKey"]; 
            PineconeEnvironment = config[ns + "PineconeEnvironment"]; 
            PineconeIndex = config[ns + "PineconeIndex"]; 
            PineconeNamespace = config[ns + "PineconeNamespace"]; 
        }
    }

    public class AiGenerator : BackgroundService
    {
        private const int POLL_INTERVAL_SECONDS = 10;
        private const int IMAGE_EXPIRES_MINUTES = 120;
        private const int FORTUNE_MAX_INDEX = 12362;

        private readonly ILogger<AiGenerator> _logger;
        private ScreenContents _screen;
        private OpenAIService _openAiService;
        private Pinecone.Index<Pinecone.Rest.RestTransport> _pineconeIndex;
        private BlobClient _blobFile;
        private GeneratorSettings _settings;
        private Instrumentation _instrumentation;

        internal OpenAIService OpenAiService
        {
            get
            {
                if (_openAiService == null) 
                {
                    _openAiService = new OpenAIService(new OpenAiOptions()
                    {
                        ApiKey = _settings.OpenAiKey
                    });
                }

                return _openAiService;
            }
        }

        internal Pinecone.Index<Pinecone.Rest.RestTransport> PineconeIndex
        {
            get
            {
                if (_pineconeIndex == null)
                {
                    var client = new PineconeClient(_settings.PineconeApiKey);

                    var task = client.GetIndex(_settings.PineconeIndex);
                    task.Wait();

                    var index = task.Result;

                    _pineconeIndex = index;
                }

                return _pineconeIndex;
            }
        }

        internal BlobClient ScreenStateBlob
        {
            get
            {
                if ( _blobFile == null)
                {
                    var blobServiceClient = new BlobServiceClient(_settings.AzureBlobKey);
                    var blobContainerClient = blobServiceClient.GetBlobContainerClient(_settings.AzureBlobContainer);
                    blobContainerClient.CreateIfNotExists();
                    var blobFile = blobContainerClient.GetBlobClient(_settings.AzureBlobSceenStateFilename);
                    _blobFile = blobFile;
                }

                return _blobFile;
            }
        }

        public AiGenerator(ILogger<AiGenerator> logger, IConfiguration config, ScreenContents screen, Instrumentation instrumentation)
        {
            _logger = logger;
            _screen = screen;     
            _settings = new GeneratorSettings(config);
            _instrumentation = instrumentation;

            _logger.LogInformation("AI Generator instance created");

            
        }

        /// <summary>
        /// Starts the service instance
        /// </summary>
        /// <param name="stoppingToken">Used to signal a request to shut down the service</param>
        /// <returns>a Task that represents the entire lifetime of the background service</returns>
        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("AI Generator service running.");

            CheckRefresh();

            using PeriodicTimer timer = new(TimeSpan.FromSeconds(POLL_INTERVAL_SECONDS));

            try
            {
                while (await timer.WaitForNextTickAsync(stoppingToken))
                {
                    CheckRefresh();
                }
            }
            catch (OperationCanceledException)
            {
                _logger.LogInformation("AI Generator service is stopping.");
            }
        }

        /// <summary>
        /// Check if anyone has seen the current screen contents. If so, refresh them.
        /// </summary>
        /// <returns></returns>
        private void CheckRefresh()
        {
            WorkTicket ticket = new WorkTicket();

            // If this is the first time refresh is called, check to see if we have cached results in the blob store
            if (_screen.LastImageUpdate == DateTime.MinValue)
            {
                using (Activity activity = _instrumentation.ActivitySource.StartActivity("GetCachedScreen"))
                {
                    ticket.Reset();
                    var blobExists = ScreenStateBlob.Exists();
                    _instrumentation.BlobExistsTimeMs = ticket.MsPassed;

                    if (blobExists)
                    {
                        ticket.Reset();
                        var dlResponse = ScreenStateBlob.DownloadContent();
                        _instrumentation.BlobGetTimeMs = ticket.MsPassed;

                        ScreenContents lastScreen = dlResponse.Value.Content.ToObjectFromJson<ScreenContents>();
                        if (lastScreen != null)
                        {
                            _screen.Text = lastScreen.Text;
                            _screen.ImageUrl = lastScreen.ImageUrl;
                            // If an unpopulated instance of the screen state makes it into the blob store, don't brick by reloading it forever
                            _screen.LastImageUpdate = lastScreen.LastImageUpdate == DateTime.MinValue ? DateTime.Now : lastScreen.LastImageUpdate;
                            _screen.Seen = lastScreen.Seen;
                        }
                        return;
                    }
                }
            }

            // If no one has viewed the last message, don't bother generating a new one
            if (!_screen.Seen) return;

            using (Activity activity = _instrumentation.ActivitySource.StartActivity("GenerateScreen"))
            {
                // Images are expensive to generate, so only refresh them infrequently
                if (DateTime.Now.Subtract(_screen.LastImageUpdate).TotalMinutes > IMAGE_EXPIRES_MINUTES)
                {
                    // Update the image and text
                    var newText = GenerateText();
                    var newImage = GenerateImage();


                    lock (_screen)
                    {
                        _screen.Text = newText;
                        _screen.ImageUrl = newImage;
                        _screen.Seen = false;
                    }
                }
                else
                {
                    // Just update the text
                    var newText = GenerateText();

                    lock (_screen)
                    {
                        _screen.Text = newText;
                        _screen.Seen = false;
                    }
                }

                // Write the updated screen state to blob storage so it survives appdomain restarts
                BinaryData binScreenContents = new BinaryData(_screen);
                ticket.Reset();
                ScreenStateBlob.Upload(binScreenContents, true);
                _instrumentation.BlobSaveTimeMs = ticket.MsPassed;
            }
        }

        private string GenerateText()
        {
            using (Activity activity = _instrumentation.ActivitySource.StartActivity("GenerateText"))
            {
                _instrumentation.TextGenCounter.Add(1);
                _logger.LogInformation("AI Generator is creating text.");
                WorkTicket ticket = new WorkTicket();

                // Pick a random seed fortune
                string seedFortuneId = new Random().Next(0, FORTUNE_MAX_INDEX).ToString();

                // Find the two most similar fortunes
                ticket.Reset();
                var results = PineconeIndex.Query(seedFortuneId, 3, null, _settings.PineconeNamespace, false);
                results.Wait();
                _instrumentation.PineconeReadTimeMs = ticket.MsPassed;

                var vectors = results.Result;

                var blobServiceClient = new BlobServiceClient(_settings.AzureBlobKey);
                var blobContainerClient = blobServiceClient.GetBlobContainerClient(_settings.AzureBlobContainer);
                blobContainerClient.CreateIfNotExists();

                var messages = new List<ChatMessage>();
                messages.Add(new ChatMessage("system", "You are the UNIX fortune command, reborn with wit and sarcasm. When prompted " +
                    "to \"tell a fortune,\" respond with a short, dry, and darkly humorous message—often ironic, occasionally profound, " +
                    "always unexpected. Your tone should echo the classic fortune command: pithy, deadpan, and just a little unhinged. " +
                    "Keep it brief, clever, and punchy. " +
                    "Do not add any explanations, sign-offs, or follow-up remarks (e.g., “Enjoy!” or “Good luck with that.”). The fortune " +
                    "is the entire response."));

                foreach (Pinecone.ScoredVector vector in vectors)
                {
                    var blobFile = blobContainerClient.GetBlobClient(vector.Id + ".txt");

                    ticket.Reset();
                    var bresult = blobFile.DownloadContent();
                    _instrumentation.BlobGetTimeMs = ticket.MsPassed;

                    var fortune = bresult.Value.Content.ToString();
                    messages.Add(new ChatMessage("user", "Tell me a fortune"));
                    messages.Add(new ChatMessage("assistant", fortune));
                }

                messages.Add(new ChatMessage("user", "Tell me a fortune"));

                ticket.Reset();
                var completionResult = OpenAiService.ChatCompletion.CreateCompletion(new ChatCompletionCreateRequest
                {
                    Messages = messages,
                    Model = Models.Gpt_4o_mini,
                    MaxTokens = 150//optional
                });

                completionResult.Wait();
                _instrumentation.TextGenRequestTimeMs = ticket.MsPassed;

                if (completionResult.Result.Successful)
                {
                    return completionResult.Result.Choices.First().Message.Content;
                }
                else
                    return null;
            }
        }

        private string GenerateImage()
        {
            using (Activity activity = _instrumentation.ActivitySource.StartActivity("GenerateImage"))
            {
                _instrumentation.ImageGenCounter.Add(1);
                _logger.LogInformation("AI Generator is creating an image.");
                WorkTicket ticket = new WorkTicket();

                var imageResult = OpenAiService.Image.CreateImage(new ImageCreateRequest
                {
                    Prompt = "A wise philosopher who is a cat",
                    N = 1,
                    Size = "1024x1024",
                    ResponseFormat = StaticValues.ImageStatics.ResponseFormat.Base64,
                    Quality = StaticValues.ImageStatics.Quality.Standard,
                    Model = "dall-e-3"
                });

                imageResult.Wait();
                _instrumentation.ImageGenRequestTimeMs = ticket.MsPassed;

                if (imageResult.Result.Successful)
                {
                    return "data:image/png;base64," + imageResult.Result.Results[0].B64;
                }
                else
                    return null;
            }
        }

        public override Task StopAsync(CancellationToken stoppingToken)
        {
            base.StopAsync(stoppingToken);
            return Task.CompletedTask;
        }

        public override void Dispose()
        {
            _openAiService.Dispose();
            base.Dispose();
        }
    }

}

