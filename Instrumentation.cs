using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace FortuneService
{
    public class Instrumentation : IDisposable
    {
        internal const string ActivitySourceName = "AiGenerator";
        internal const string MeterName = "FortuneCat";
        private readonly Meter meter;

        public Instrumentation()
        {
            string? version = typeof(Instrumentation).Assembly.GetName().Version?.ToString();
            this.ActivitySource = new ActivitySource(ActivitySourceName, version);
            this.meter = new Meter(MeterName, version);
            this.ImageGenCounter = this.meter.CreateCounter<long>("openai.imagegencount", description: "The number of times an image was generated");
            this.TextGenCounter = this.meter.CreateCounter<long>("openai.textgencount", description: "The number of times text was generated");

            this.meter.CreateObservableGauge<double>("openai.imagegenlatency_ms", () => [new Measurement<double>(this.ImageGenRequestTimeMs)], description: "Latency of each image generation call to DALL-E");
            this.meter.CreateObservableGauge<double>("openai.textgenlatency_ms", () => [new Measurement<double>(this.TextGenRequestTimeMs)], description: "Latency of each text generation call to DALL-E");
            this.meter.CreateObservableGauge<double>("blob.fetchlatency_ms", () => [new Measurement<double>(this.BlobGetTimeMs)], description: "Latency of each blob retrieval request to Azure blob store");
            this.meter.CreateObservableGauge<double>("blob.savelatency_ms", () => [new Measurement<double>(this.BlobSaveTimeMs)], description: "Latency of each blob upload request to Azure blob store");
            this.meter.CreateObservableGauge<double>("blob.existslatency_ms", () => [new Measurement<double>(this.BlobExistsTimeMs)], description: "Latency of each blob existence check request to Azure blob store");
            this.meter.CreateObservableGauge<double>("pinecone.readlatency_ms", () => [new Measurement<double>(this.PineconeReadTimeMs)], description: "Latency of each vector query to Pinecone");
        }

        public ActivitySource ActivitySource { get; }

        public Counter<long> ImageGenCounter { get; }
        public Counter<long> TextGenCounter { get; }

        public double ImageGenRequestTimeMs { get; set; }
        public double TextGenRequestTimeMs { get; set; }
        public double BlobGetTimeMs { get; set; }
        public double BlobSaveTimeMs { get; set; }
        public double BlobExistsTimeMs { get; set; }
        public double PineconeReadTimeMs { get; set; }

        public void Dispose()
        {
            this.ActivitySource.Dispose();
            this.meter.Dispose();
        }
    }

    public class WorkTicket
    {
        private DateTime _start;
        public WorkTicket()
        {
            Reset();
        }

        public void Reset()
        {
            _start = DateTime.Now;
        }

        public double MsPassed
        {
            get { return DateTime.Now.Subtract(_start).TotalMilliseconds; }
        }
    }
}
