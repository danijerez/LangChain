﻿using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Amazon.BedrockRuntime.Model;
using LangChain.Providers.Amazon.Bedrock.Internal;

// ReSharper disable once CheckNamespace
namespace LangChain.Providers.Amazon.Bedrock;

public class AnthropicClaudeChatModel(
    BedrockProvider provider,
    string id)
    : ChatModel(id)
{
    /// <summary>
    /// Generates a chat response based on the provided `ChatRequest`.
    /// </summary>
    /// <param name="request">The `ChatRequest` containing the input messages and other parameters.</param>
    /// <param name="settings">Optional `ChatSettings` to override the model's default settings.</param>
    /// <param name="cancellationToken">A cancellation token to cancel the operation.</param>
    /// <returns>A `ChatResponse` containing the generated messages and usage information.</returns>
    public override async Task<ChatResponse> GenerateAsync(
        ChatRequest request,
        ChatSettings? settings = null,
        CancellationToken cancellationToken = default)
    {
        request = request ?? throw new ArgumentNullException(nameof(request));

        var watch = Stopwatch.StartNew();
        var prompt = request.Messages.ToRolePrompt();
        var messages = request.Messages.ToList();

        var stringBuilder = new StringBuilder();

        var usedSettings = AnthropicChatSettings.Calculate(
            requestSettings: settings,
            modelSettings: Settings,
            providerSettings: provider.ChatSettings);

        var bodyJson = CreateBodyJson(prompt, usedSettings);

        if (usedSettings.UseStreaming == true)
        {
            var streamRequest = BedrockModelRequest.CreateStreamRequest(Id, bodyJson);
            InvokeModelWithResponseStreamResponse? response = await provider.Api.InvokeModelWithResponseStreamAsync(streamRequest, cancellationToken).ConfigureAwait(false);

            foreach (var payloadPart in response.Body)
            {
                var streamEvent = (PayloadPart)payloadPart;
                var chunk = await JsonSerializer.DeserializeAsync<JsonObject>(streamEvent.Bytes, cancellationToken: cancellationToken)
                    .ConfigureAwait(false);
                var delta = chunk?["completion"]!.GetValue<string>();

                OnPartialResponseGenerated(delta!);
                stringBuilder.Append(delta);

                var finished = chunk?["completionReason"]?.GetValue<string>();
                if (finished?.ToUpperInvariant() == "FINISH")
                {
                    break;
                }
            }

            OnPartialResponseGenerated(Environment.NewLine);
            stringBuilder.Append(Environment.NewLine);

            var newMessage = new Message(
                Content: stringBuilder.ToString(),
                Role: MessageRole.Ai);
            messages.Add(newMessage);

            OnCompletedResponseGenerated(newMessage.Content);
        }
        else
        {
            var response = await provider.Api.InvokeModelAsync(Id, bodyJson, cancellationToken).ConfigureAwait(false);

            var generatedText = response?["completion"]?.GetValue<string>() ?? "";

            messages.Add(generatedText.AsAiMessage());
            OnCompletedResponseGenerated(generatedText);
        }

        var usage = Usage.Empty with
        {
            Time = watch.Elapsed,
        };
        AddUsage(usage);
        provider.AddUsage(usage);

        return new ChatResponse
        {
            Messages = messages,
            UsedSettings = usedSettings,
            Usage = usage,
        };
    }

    /// <summary>
    /// Creates the request body JSON for the Anthropic model based on the provided prompt and settings.
    /// </summary>
    /// <param name="prompt">The input prompt for the model.</param>
    /// <param name="usedSettings">The settings to use for the request.</param>
    /// <returns>A `JsonObject` representing the request body.</returns>
    private static JsonObject CreateBodyJson(string prompt, AnthropicChatSettings usedSettings)
    {
        var bodyJson = new JsonObject
        {
            ["prompt"] = prompt,
            ["max_tokens_to_sample"] = usedSettings.MaxTokens!.Value,
            ["temperature"] = usedSettings.Temperature!.Value,
            ["top_p"] = usedSettings.TopP!.Value,
            ["top_k"] = usedSettings.TopK!.Value,
            ["stop_sequences"] = new JsonArray("\n\nHuman:")
        };
        return bodyJson;
    }
}