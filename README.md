FortuneService is an Azure function which services http://dreamlands.org/fortunecat

It uses the OpenAI API to generate a fortune cookie message and an image of a contemplative cat.

To keep the website responsive, content is served from an Azure blob storage cache. When the content is 
stale (older than 2 hours for the image, older than 5 seconds for the fortune), the function will serve
from the cache, close the HTTP connection, then generate new content and update the cache.

When this app was built, the frontier OpenAI model was gpt3.5-turbo. Even with considerable prompt 
tuning, the model was not able to generate a fortune cookie message that was not a generic platitude.

To correct this, the app randomly selects the index of a fortune cookie message from the BSD
fortune cookie database. It retrieves a cached embedding (generaged by TextEmbeddingAdaV2) for that
message and queries a Pinecone index for two similar fortunes. It then generates a multi-shot 
prompt, asking the model to generate a fortune cookie message that is similar to the three example 
fortunes.

GPT4 is much better at generating fortune cookie messages, and including the examples does not seem
to significantly improve the results. However, this code path is left in as an example of using RAG
to improve results.

The app is instrumented via OpenTelemetry, and the telemetry is sent to a Grafana cloud instance.