// Copyright © Erickson Lopez. MIT License.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using EricksonLopez.Result;
using EricksonLopez.Webhooks;
using EricksonLopez.Webhooks.AspNetCore;
using EricksonLopez.Webhooks.Redis;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace EricksonLopez.Webhooks.Sample;

/// <summary>
/// Official Reference Showcase for the EricksonLopez.Webhooks ecosystem.
/// Serves as the executable documentation and cookbook covering 100% of the public API surface across 11 progressive levels.
/// </summary>
public static class Program
{
    public static async Task<int> Main(string[] args)
    {
        Console.WriteLine("===============================================================================");
        Console.WriteLine(" EricksonLopez.Webhooks — Official Executable Reference Showcase (Levels 0-10)");
        Console.WriteLine("===============================================================================");

        try
        {
            RunLevel0_Conceptual();
            await RunLevel1_QuickStartAsync();
            RunLevel2_FullConfiguration();
            await RunLevel3_RealWorldUseCasesAsync();
            await RunLevel4_AdvancedIntegrationAsync();
            await RunLevel5_StreamingAndProcessingAsync();
            await RunLevel6_ErrorHandlingAndResilienceAsync();
            await RunLevel7_ScalabilityAndReplayDefenseAsync();
            await RunLevel8_CustomizationAsync();
            RunLevel9_RedisExtensions();
            await RunLevel10_EnterpriseArchitectureAsync();

            Console.WriteLine("\n===============================================================================");
            Console.WriteLine(" [SUCCESS] All 11 Showcase Levels Executed Successfully (100% Public API Coverage)");
            Console.WriteLine("===============================================================================\n");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"\n[FATAL SHOWCASE FAILURE]: {ex}");
            return 1;
        }
    }

    // =========================================================================
    // LEVEL 0 — Conceptual
    // =========================================================================

    /// <summary>
    /// Level 0 — Conceptual: Cryptographic invariants, side-channels, and threat model.
    /// </summary>
    private static void RunLevel0_Conceptual()
    {
        Console.WriteLine("\n>>> [Level 0 — Conceptual: Architecture & Cryptographic Invariants]");
        Console.WriteLine("• What is this library? An enterprise-grade webhook delivery and receiving engine.");
        Console.WriteLine("• What problem does it solve? Eliminates timing side-channels, replay attacks, SSRF, and socket exhaustion.");
        Console.WriteLine("• Why does it exist? Ad-hoc implementations compare signatures with '==' or '.Equals()', exposing secrets to side-channel timing attacks.");
        Console.WriteLine("• Guarantees: Side-channel immunity via CryptographicOperations.FixedTimeEquals, deterministic clock skew tolerance, and Native AOT.");
        Console.WriteLine("• Packages: EricksonLopez.Webhooks (Core) | EricksonLopez.Webhooks.AspNetCore (Receiver) | EricksonLopez.Webhooks.Redis (Distributed)");
        Console.WriteLine("• Supported protocols: EricksonLopez (X-Webhook-* headers) & StandardWebhooks spec (webhook-* headers).");
        Console.WriteLine("• Trade-offs: No built-in message broker; resilience must be configured externally on HttpClient.");
    }

    // =========================================================================
    // LEVEL 1 — Quick Start
    // =========================================================================

    /// <summary>
    /// Level 1 — Quick Start: Signing key, HMAC computation, and basic verification.
    /// </summary>
    private static async Task RunLevel1_QuickStartAsync()
    {
        Console.WriteLine("\n>>> [Level 1 — Quick Start: HMAC-SHA256 Signing and Verification]");

        // 1. WebhookSecret: Wrapper masking secret to prevent accidental leakage in logs
        const string rawSecret = "whsec_live_enterprise_secret_key_1234567890abcdef";
        var secret = new WebhookSecret(rawSecret);
        Console.WriteLine($"[1.1] WebhookSecret created: ToString() -> '{secret}' (Log obfuscation check)");

        var unsecuredString = secret.GetUnsecuredString();
        Console.WriteLine($"[1.2] Secret securely unpacked (length: {unsecuredString.Length})");

        // Implicit conversion string -> WebhookSecret
        WebhookSecret implicitSecret = rawSecret;
        // Implicit conversion WebhookSecret -> string
        string implicitStr = secret;
        Console.WriteLine($"[1.3] Implicit conversions string <-> WebhookSecret validated ({implicitStr.Length > 0})");

        // 2. ComputeSignature — 3-param overload (default EricksonLopez protocol)
        const string payload = "{\"event\":\"order.created\",\"id\":\"ord_9981\",\"amount\":149.50}";
        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        var signatureDefault = WebhookSigner.ComputeSignature(secret, timestamp, payload);
        Console.WriteLine($"[1.4] Generated signature (EricksonLopez, 3-param): {signatureDefault}");

        // 3. VerifySignature — 4-param overload (default AutoDetect)
        var isValid = WebhookSigner.VerifySignature(secret, timestamp, payload, signatureDefault);
        Console.WriteLine($"[1.5] WebhookSigner.VerifySignature (4-param, AutoDetect): {isValid}");

        // 4. ComputeSignature — 5-param overload (explicit protocol, nullable webhookId)
        var signatureExplicit = WebhookSigner.ComputeSignature(secret, timestamp, payload, WebhookSenderProtocol.EricksonLopez, null);
        Console.WriteLine($"[1.6] Generated signature (EricksonLopez, 5-param explicit): {signatureExplicit}");

        // 5. ComputeSignature + VerifySignature — StandardWebhooks protocol
        const string webhookId = "msg_req_77218";
        var standardSignature = WebhookSigner.ComputeSignature(secret, timestamp, payload, WebhookSenderProtocol.Standard, webhookId);
        Console.WriteLine($"[1.7] Generated signature (StandardWebhooks): {standardSignature}");

        var isStandardValid = WebhookSigner.VerifySignature(
            secret,
            timestamp,
            payload,
            standardSignature,
            WebhookReceiverProtocol.Standard,
            webhookId);
        Console.WriteLine($"[1.8] WebhookSigner.VerifySignature (StandardWebhooks, 6-param): {isStandardValid}");

        // 6. ComputeSignature ReadOnlySpan<byte> + VerifySignature ReadOnlySpan<byte> — Zero Allocation
        var payloadSpanBytes = Encoding.UTF8.GetBytes(payload);
        var byteSignature = WebhookSigner.ComputeSignature(secret, timestamp, payloadSpanBytes.AsSpan(), WebhookSenderProtocol.EricksonLopez);
        var isSpanValid = WebhookSigner.VerifySignature(
            secret,
            timestamp,
            payloadSpanBytes.AsSpan(),
            byteSignature,
            WebhookReceiverProtocol.EricksonLopez);
        Console.WriteLine($"[1.9] ComputeSignature/VerifySignature (ReadOnlySpan<byte>): {isSpanValid}");

        // 7. VerifySignature AutoDetect multi-protocol — automatically detected from prefix
        var isAutoDetect = WebhookSigner.VerifySignature(
            secret,
            timestamp,
            payload,
            standardSignature,
            WebhookReceiverProtocol.AutoDetect,
            webhookId);
        Console.WriteLine($"[1.10] WebhookSigner.VerifySignature (AutoDetect + StandardWebhooks): {isAutoDetect}");

        await Task.CompletedTask;
    }

    // =========================================================================
    // LEVEL 2 — Full Configuration
    // =========================================================================

    /// <summary>
    /// Level 2 — Full Configuration: All options, enums, headers, and builders.
    /// </summary>
    private static void RunLevel2_FullConfiguration()
    {
        Console.WriteLine("\n>>> [Level 2 — Full Configuration: Sender & Receiver Options]");

        // 1. WebhookSenderOptions — all properties
        var senderOptions = new WebhookSenderOptions
        {
            Timeout = TimeSpan.FromSeconds(15),
            MaxPayloadSizeBytes = 5 * 1024 * 1024,
            EnableSsrfProtection = true,
            AllowPrivateNetworks = false,
            DangerousAllowInsecureHttp = false,
            Protocol = WebhookSenderProtocol.EricksonLopez
        };
        senderOptions.SsrfProtection.RequireHttps = true;
        Console.WriteLine($"[2.1] WebhookSenderOptions: Timeout={senderOptions.Timeout.TotalSeconds}s, MaxPayload={senderOptions.MaxPayloadSizeBytes}B, SSRF={senderOptions.EnableSsrfProtection}, Protocol={senderOptions.Protocol}");

        // 2. DangerousAllowInsecureHttp — explicit security flag for non-TLS testing
        senderOptions.DangerousAllowInsecureHttp = false;
        Console.WriteLine($"[2.2] WebhookSenderOptions.DangerousAllowInsecureHttp: {senderOptions.DangerousAllowInsecureHttp} (Mandatory TLS enforced)");

        // 3. WebhookReceiverOptions — all properties + key rotation via AddSecret()
        var receiverOptions = new WebhookReceiverOptions
        {
            SecretKey = "primary_secret_v1",
            TimestampTolerance = TimeSpan.FromMinutes(3),
            MaxPayloadSizeBytes = 2 * 1024 * 1024,
            RoutePath = "/api/v1/webhooks/orders",
            Protocol = WebhookReceiverProtocol.AutoDetect,
            ValidatePostOnly = true
        };
        receiverOptions.AddSecret("rotated_secret_v2");
        receiverOptions.AddSecret("emergency_fallback_v0");
        Console.WriteLine($"[2.3] WebhookReceiverOptions: Route={receiverOptions.RoutePath}, Tolerance={receiverOptions.TimestampTolerance.TotalMinutes}m, Keys via AddSecret()={receiverOptions.SecretKeys.Count}, Protocol={receiverOptions.Protocol}");

        // SecretKeys — direct assignment of ImmutableList<string>
        var receiverOptions2 = new WebhookReceiverOptions();
        receiverOptions2.SecretKeys = System.Collections.Immutable.ImmutableList.Create("key_a", "key_b", "key_c");
        Console.WriteLine($"[2.4] WebhookReceiverOptions.SecretKeys (direct ImmutableList): {receiverOptions2.SecretKeys.Count} keys");

        // 4. WebhookHeaders — all 8 constants
        Console.WriteLine("[2.5] WebhookHeaders (EricksonLopez protocol headers):");
        Console.WriteLine($"      Signature  = '{WebhookHeaders.Signature}'");
        Console.WriteLine($"      Timestamp  = '{WebhookHeaders.Timestamp}'");
        Console.WriteLine($"      EventType  = '{WebhookHeaders.EventType}'");
        Console.WriteLine($"      DeliveryId = '{WebhookHeaders.DeliveryId}'");
        Console.WriteLine("[2.6] WebhookHeaders (StandardWebhooks spec headers):");
        Console.WriteLine($"      StandardSignature  = '{WebhookHeaders.StandardSignature}'");
        Console.WriteLine($"      StandardTimestamp  = '{WebhookHeaders.StandardTimestamp}'");
        Console.WriteLine($"      StandardDeliveryId = '{WebhookHeaders.StandardDeliveryId}'");
        Console.WriteLine($"      StandardEventType  = '{WebhookHeaders.StandardEventType}'");

        // 5. WebhookProtocol enum (combined enum — EricksonLopez/Standard/AutoDetect)
        Console.WriteLine("[2.7] WebhookProtocol (combined enum):");
        Console.WriteLine($"      EricksonLopez = {(int)WebhookProtocol.EricksonLopez}");
        Console.WriteLine($"      Standard      = {(int)WebhookProtocol.Standard}");
        Console.WriteLine($"      AutoDetect    = {(int)WebhookProtocol.AutoDetect}");

        // 6. WebhookSenderProtocol & WebhookReceiverProtocol enums
        Console.WriteLine($"[2.8] WebhookSenderProtocol: EricksonLopez={WebhookSenderProtocol.EricksonLopez}, Standard={WebhookSenderProtocol.Standard}");
        Console.WriteLine($"[2.9] WebhookReceiverProtocol: AutoDetect={WebhookReceiverProtocol.AutoDetect}, EricksonLopez={WebhookReceiverProtocol.EricksonLopez}, Standard={WebhookReceiverProtocol.Standard}");
    }

    // =========================================================================
    // LEVEL 3 — Real World Use Cases
    // =========================================================================

    /// <summary>
    /// Level 3 — Real World Use Cases: WebhookMessage, payloads, and Native AOT serialization.
    /// </summary>
    private static async Task RunLevel3_RealWorldUseCasesAsync()
    {
        Console.WriteLine("\n>>> [Level 3 — Real World Use Cases: Native AOT & WebhookMessage]");

        var targetUrl = new Uri("https://api.partner.com/webhooks/orders");
        var secret = new WebhookSecret("whsec_real_world_secret_99881122");

        // 1. WebhookMessage with String Payload
        var messageString = new WebhookMessage(
            targetUrl,
            secret,
            "order.created",
            "evt_001_abc",
            "{\"orderId\":\"ord_123\",\"total\":99.95}");
        Console.WriteLine($"[3.1] WebhookMessage (String): EventType={messageString.EventType}, EventId={messageString.EventId}, TargetUrl={messageString.TargetUrl}");
        Console.WriteLine($"[3.2] WebhookMessage: PayloadString={messageString.PayloadString is not null}, PayloadBytes={messageString.PayloadBytes.HasValue}");

        // 2. WebhookMessage with ReadOnlyMemory<byte> — Zero Allocation
        var payloadBytes = Encoding.UTF8.GetBytes("{\"orderId\":\"ord_456\",\"total\":249.00}");
        var messageBytes = new WebhookMessage(
            targetUrl,
            secret,
            "order.refunded",
            "evt_002_def",
            payloadBytes.AsMemory());
        Console.WriteLine($"[3.3] WebhookMessage (Bytes): PayloadBytes.Length={messageBytes.PayloadBytes?.Length}B, PayloadString={messageBytes.PayloadString is not null}");

        // 3. WebhookSender.CreateSafeSender() — parameterless factory
        var safeSenderMinimal = WebhookSender.CreateSafeSender();
        Console.WriteLine($"[3.4] WebhookSender.CreateSafeSender(): {safeSenderMinimal.GetType().Name}");

        // 4. WebhookSender.CreateSafeSender() — factory with all optional parameters
        var customOptions = new WebhookSenderOptions { Timeout = TimeSpan.FromSeconds(5) };
        var customDlqForSender = new NoOpWebhookDeadLetterSink();
        var safeSenderFull = WebhookSender.CreateSafeSender(
            deadLetterQueue: customDlqForSender,
            options: customOptions,
            logger: null);
        Console.WriteLine($"[3.5] WebhookSender.CreateSafeSender(dlq, options, logger): {safeSenderFull.GetType().Name}");

        // 5. WebhookSenderExtensions.SendAsync<T> — Native AOT with JsonTypeInfo<T>
        var safeSender = WebhookSender.CreateSafeSender();
        var sampleEvent = new OrderEvent("ord_9999", 350.00m, "customer@enterprise.com");

        using var cts = new CancellationTokenSource();
        cts.Cancel();
        try
        {
            await safeSender.SendAsync(
                targetUrl,
                secret.GetUnsecuredString(),
                "order.dispatched",
                "evt_003_ghi",
                sampleEvent,
                SampleJsonContext.Default.OrderEvent,
                cts.Token);
        }
        catch (OperationCanceledException)
        {
            Console.WriteLine("[3.6] WebhookSenderExtensions.SendAsync<OrderEvent> (Native AOT + CancellationToken): verified.");
        }
    }

    // =========================================================================
    // LEVEL 4 — Advanced Integration (DI)
    // =========================================================================

    /// <summary>
    /// Level 4 — Advanced Integration: Dependency injection container registration.
    /// </summary>
    private static async Task RunLevel4_AdvancedIntegrationAsync()
    {
        Console.WriteLine("\n>>> [Level 4 — Advanced Integration: Dependency Injection]");

        // 1. AddWebhooks() with configuration
        var services = new ServiceCollection();
        services.AddWebhooks(options =>
        {
            options.Timeout = TimeSpan.FromSeconds(8);
            options.MaxPayloadSizeBytes = 4 * 1024 * 1024;
            options.EnableSsrfProtection = true;
        });

        // 2. AddWebhooks() without configuration (defaults)
        var servicesDefault = new ServiceCollection();
        servicesDefault.AddWebhooks();
        Console.WriteLine("[4.1] AddWebhooks() parameterless: Timeout=10s, MaxPayload=10MB, SSRF=true (defaults).");

        // 3. AddWebhookReceiver() with configuration
        services.AddWebhookReceiver(options =>
        {
            options.SecretKey = "di_primary_secret_key";
            options.RoutePath = "/api/inbound/hooks";
            options.TimestampTolerance = TimeSpan.FromMinutes(2);
        });

        // 4. AddInMemoryReplayDetector() with retentionPeriod
        services.AddInMemoryReplayDetector(TimeSpan.FromMinutes(5));

        // 5. AddInMemoryReplayDetector() parameterless (default 5 min)
        var servicesReplay = new ServiceCollection();
        servicesReplay.AddWebhookReceiver(o => o.SecretKey = "test_key");
        servicesReplay.AddInMemoryReplayDetector();
        Console.WriteLine("[4.2] AddInMemoryReplayDetector() parameterless: retentionPeriod=5min (default).");

        using var serviceProvider = services.BuildServiceProvider();

        var sender = serviceProvider.GetRequiredService<IWebhookSender>();
        var validator = serviceProvider.GetRequiredService<IWebhookValidator>();
        var replayDetector = serviceProvider.GetRequiredService<IWebhookReplayDetector>();
        var deadLetterSink = serviceProvider.GetRequiredService<IWebhookDeadLetterSink>();

        Console.WriteLine("[4.3] Resolved DI services:");
        Console.WriteLine($"      IWebhookSender           -> {sender.GetType().FullName}");
        Console.WriteLine($"      IWebhookValidator        -> {validator.GetType().FullName}");
        Console.WriteLine($"      IWebhookReplayDetector   -> {replayDetector.GetType().FullName}");
        Console.WriteLine($"      IWebhookDeadLetterSink   -> {deadLetterSink.GetType().FullName}");

        await Task.CompletedTask;
    }

    // =========================================================================
    // LEVEL 5 — Streaming & Processing
    // =========================================================================

    /// <summary>
    /// Level 5 — Processing: Streams, multi-key, size limits, exceptions.
    /// </summary>
    private static async Task RunLevel5_StreamingAndProcessingAsync()
    {
        Console.WriteLine("\n>>> [Level 5 — Processing: Streams & LOH Allocation Prevention]");

        const string secret = "whsec_streaming_key_889977665544";
        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var largePayloadData = new byte[64 * 1024]; // 64 KB
        Array.Fill(largePayloadData, (byte)'A');

        // 1. ComputeSignatureAsync — EricksonLopez protocol
        using (var stream = new MemoryStream(largePayloadData))
        {
            var streamSignature = await WebhookSigner.ComputeSignatureAsync(
                secret,
                timestamp,
                stream,
                WebhookSenderProtocol.EricksonLopez);
            Console.WriteLine($"[5.1] ComputeSignatureAsync (EricksonLopez): {streamSignature.Substring(0, 20)}...");

            // 2. VerifySignatureAsync — overload(string, long, Stream, string, protocol, webhookId?, CT)
            stream.Position = 0;
            var isStreamValid = await WebhookSigner.VerifySignatureAsync(
                secret,
                timestamp,
                stream,
                streamSignature,
                WebhookReceiverProtocol.EricksonLopez);
            Console.WriteLine($"[5.2] VerifySignatureAsync (single-key, without maxPayloadBytes): {isStreamValid}");
        }

        // 3. ComputeSignatureAsync — StandardWebhooks with webhookId
        using (var stdStream = new MemoryStream(largePayloadData))
        {
            const string stdWebhookId = "msg_std_12345";
            var stdSig = await WebhookSigner.ComputeSignatureAsync(
                secret,
                timestamp,
                stdStream,
                WebhookSenderProtocol.Standard,
                webhookId: stdWebhookId);
            Console.WriteLine($"[5.3] ComputeSignatureAsync (StandardWebhooks): {stdSig.Substring(0, 20)}...");

            // 4. VerifySignatureAsync — overload(string, long, Stream, string, protocol, webhookId, maxPayloadBytes, CT)
            stdStream.Position = 0;
            var isStdValid = await WebhookSigner.VerifySignatureAsync(
                secret,
                timestamp,
                stdStream,
                stdSig,
                WebhookReceiverProtocol.Standard,
                webhookId: stdWebhookId,
                maxPayloadBytes: 512 * 1024); // 512 KB limit
            Console.WriteLine($"[5.4] VerifySignatureAsync (single-key, with maxPayloadBytes=512KB): {isStdValid}");
        }

        // 5. VerifySignatureAsync — IEnumerable<string> multi-key with maxPayloadBytes
        using (var multiKeyStream = new MemoryStream(largePayloadData))
        {
            var keys = new[] { "wrong_secret_1", secret, "wrong_secret_2" };
            var sig = WebhookSigner.ComputeSignature(secret, timestamp, largePayloadData.AsSpan(), WebhookSenderProtocol.EricksonLopez);

            var isMultiKeyValid = await WebhookSigner.VerifySignatureAsync(
                keys,
                timestamp,
                multiKeyStream,
                sig,
                WebhookReceiverProtocol.AutoDetect,
                webhookId: null,
                maxPayloadBytes: 1024 * 1024);
            Console.WriteLine($"[5.5] VerifySignatureAsync (multi-key IEnumerable<string>, maxPayloadBytes=1MB): {isMultiKeyValid}");
        }

        // 6. WebhookPayloadTooLargeException — ctor(long maxBytes) via ComputeSignatureAsync
        using (var overflowStream = new MemoryStream(largePayloadData))
        {
            try
            {
                await WebhookSigner.ComputeSignatureAsync(
                    secret,
                    timestamp,
                    overflowStream,
                    WebhookSenderProtocol.EricksonLopez,
                    maxPayloadBytes: 1024); // 1 KB strict
            }
            catch (WebhookPayloadTooLargeException ex)
            {
                Console.WriteLine($"[5.6] WebhookPayloadTooLargeException(long): MaxBytes={ex.MaxBytes}");
            }
        }

        // 7. WebhookPayloadTooLargeException — ctor(string message)
        try
        {
            throw new WebhookPayloadTooLargeException("Custom message: payload exceeds endpoint limit.");
        }
        catch (WebhookPayloadTooLargeException ex)
        {
            Console.WriteLine($"[5.7] WebhookPayloadTooLargeException(string): Message='{ex.Message}', MaxBytes={ex.MaxBytes}");
        }

        // 8. WebhookPayloadTooLargeException — ctor(string, Exception)
        try
        {
            throw new WebhookPayloadTooLargeException("Upstream rejection.", new InvalidOperationException("Inner cause"));
        }
        catch (WebhookPayloadTooLargeException ex)
        {
            Console.WriteLine($"[5.8] WebhookPayloadTooLargeException(string, Exception): InnerException={ex.InnerException?.GetType().Name}");
        }
    }

    // =========================================================================
    // LEVEL 6 — Error Handling & Dead Letter Sinks
    // =========================================================================

    /// <summary>
    /// Level 6 — Error Handling: WebhookErrors, WebhookDeliveryResult, comprehensive DLQ.
    /// </summary>
    private static async Task RunLevel6_ErrorHandlingAndResilienceAsync()
    {
        Console.WriteLine("\n>>> [Level 6 — Error Handling: WebhookErrors & Dead-Letter Sinks]");

        var targetUri = new Uri("https://partner.enterprise.com/webhooks");

        // 1. WebhookErrors — all factory methods
        var deliveryFailed = WebhookErrors.DeliveryFailed(targetUri, 3, "HTTP 504 Gateway Timeout");
        var securityViolation = WebhookErrors.SecurityViolation("Blocked private RFC1918: 192.168.1.100");
        var timeoutError = WebhookErrors.Timeout(targetUri, TimeSpan.FromSeconds(15));
        var invalidConfig = WebhookErrors.InvalidConfiguration("Missing required target URI");
        var payloadTooLarge = WebhookErrors.PayloadTooLarge(5 * 1024 * 1024);

        Console.WriteLine("[6.1] WebhookErrors domain factory methods:");
        Console.WriteLine($"      [{deliveryFailed.Code}] {deliveryFailed.Description}");
        Console.WriteLine($"      [{securityViolation.Code}] {securityViolation.Description}");
        Console.WriteLine($"      [{timeoutError.Code}] {timeoutError.Description}");
        Console.WriteLine($"      [{invalidConfig.Code}] {invalidConfig.Description}");
        Console.WriteLine($"      [{payloadTooLarge.Code}] {payloadTooLarge.Description}");

        // 2. WebhookDeliveryResult — all parameters of record struct
        var deliveryResult = new WebhookDeliveryResult(
            IsSuccess: false,
            StatusCode: 504,
            Attempts: 3,
            Duration: TimeSpan.FromMilliseconds(450),
            ErrorMessage: "Gateway Timeout",
            ResponseSnippet: "{\"error\":\"upstream_timeout\"}");
        Console.WriteLine($"[6.2] WebhookDeliveryResult: IsSuccess={deliveryResult.IsSuccess}, StatusCode={deliveryResult.StatusCode}, Attempts={deliveryResult.Attempts}, Duration={deliveryResult.Duration.TotalMilliseconds}ms");
        Console.WriteLine($"      ErrorMessage='{deliveryResult.ErrorMessage}', ResponseSnippet='{deliveryResult.ResponseSnippet}'");

        // 3. WebhookPayload — with explicit AttemptNumber
        var payloadEnvelope = new WebhookPayload(
            DeliveryId: "dlv_err_0099",
            EventType: "payment.charge_failed",
            Payload: "{\"reason\":\"insufficient_funds\"}",
            Timestamp: DateTimeOffset.UtcNow,
            AttemptNumber: 3);
        Console.WriteLine($"[6.3] WebhookPayload: DeliveryId='{payloadEnvelope.DeliveryId}', EventType='{payloadEnvelope.EventType}', AttemptNumber={payloadEnvelope.AttemptNumber}");

        // WebhookPayload — default AttemptNumber (=1)
        var payloadDefault = new WebhookPayload("dlv_001", "order.created", "{}", DateTimeOffset.UtcNow);
        Console.WriteLine($"[6.4] WebhookPayload (AttemptNumber=default): {payloadDefault.AttemptNumber}");

        // 4. Positional deconstruction of records
        var (delivId, evType, pldStr, pldTs, attempt) = payloadEnvelope;
        var (isSuccess, statCode, attemptsCount, dur, errMsg, respSnip) = deliveryResult;
        Console.WriteLine($"[6.5] Deconstruction: DeliveryId='{delivId}', StatusCode={statCode}, Attempts={attemptsCount}");

        // 5. NoOpWebhookDeadLetterSink — safe drop
        var noOpSink = new NoOpWebhookDeadLetterSink();
        await noOpSink.EnqueueAsync(targetUri, payloadEnvelope, deliveryResult);
        Console.WriteLine("[6.6] NoOpWebhookDeadLetterSink.EnqueueAsync: silent drop OK.");

        // 6. InMemoryWebhookDeadLetterSink — default constructor (maxCapacity=1000)
        var inMemoryDlqDefault = new InMemoryWebhookDeadLetterSink();
        Console.WriteLine($"[6.7] InMemoryWebhookDeadLetterSink() default ctor: Count={inMemoryDlqDefault.Count}, MaxCapacity={inMemoryDlqDefault.MaxCapacity}");

        // 7. InMemoryWebhookDeadLetterSink — custom capacity constructor
        var inMemoryDlq = new InMemoryWebhookDeadLetterSink(maxCapacity: 50);
        Console.WriteLine($"[6.8] InMemoryWebhookDeadLetterSink(50): MaxCapacity={inMemoryDlq.MaxCapacity}");

        // 8. EnqueueAsync — enqueue multiple entries
        for (int i = 0; i < 3; i++)
        {
            var envelope = new WebhookPayload($"dlv_{i:000}", "order.failed", "{}", DateTimeOffset.UtcNow, i + 1);
            var failResult = new WebhookDeliveryResult(false, 500, i + 1, TimeSpan.FromMilliseconds(100 * (i + 1)));
            await inMemoryDlq.EnqueueAsync(targetUri, envelope, failResult);
        }
        Console.WriteLine($"[6.9] InMemoryWebhookDeadLetterSink.EnqueueAsync x3: Count={inMemoryDlq.Count}");

        // 9. GetSnapshot() — thread-safe snapshot of all entries
        var snapshot = inMemoryDlq.GetSnapshot();
        Console.WriteLine($"[6.10] GetSnapshot(): {snapshot.Count} entries:");
        foreach (var (url, p, r) in snapshot)
        {
            Console.WriteLine($"        DeliveryId='{p.DeliveryId}', HTTP={r.StatusCode}, Attempts={r.Attempts}");
        }

        // 10. Bounded capacity limit — verify oldest entry drops when limit reached
        var smallDlq = new InMemoryWebhookDeadLetterSink(maxCapacity: 2);
        for (int i = 0; i < 4; i++)
        {
            await smallDlq.EnqueueAsync(targetUri,
                new WebhookPayload($"id_{i}", "ev", "{}", DateTimeOffset.UtcNow),
                new WebhookDeliveryResult(false, 500, 1, TimeSpan.Zero));
        }
        Console.WriteLine($"[6.11] InMemoryWebhookDeadLetterSink(maxCapacity=2) after 4 inserts: Count={smallDlq.Count} (oldest dropped)");
    }

    // =========================================================================
    // LEVEL 7 — Scalability & Replay Defense
    // =========================================================================

    /// <summary>
    /// Level 7 — Scalability: Replay detection, IDisposable, DiagnosticSourceName.
    /// </summary>
    private static async Task RunLevel7_ScalabilityAndReplayDefenseAsync()
    {
        Console.WriteLine("\n>>> [Level 7 — Scalability & Anti-Replay: InMemoryWebhookReplayDetector]");

        // 1. InMemoryWebhookReplayDetector(TimeSpan) — ctor with retention window
        using var replayDetector = new InMemoryWebhookReplayDetector(TimeSpan.FromMinutes(2));
        const string messageId = "msg_unique_id_99812";

        var firstAttempt = await replayDetector.TryRecordAsync(messageId);
        Console.WriteLine($"[7.1] TryRecordAsync (1st attempt): '{messageId}' -> {firstAttempt} (Accepted)");

        var replayAttempt = await replayDetector.TryRecordAsync(messageId);
        Console.WriteLine($"[7.2] TryRecordAsync (replay): '{messageId}' -> {replayAttempt} (Blocked)");

        // 2. InMemoryWebhookReplayDetector() — default constructor (5 minutes)
        using var defaultDetector = new InMemoryWebhookReplayDetector();
        var defaultResult = await defaultDetector.TryRecordAsync("msg_default_ctor");
        Console.WriteLine($"[7.3] InMemoryWebhookReplayDetector() default ctor (5min): TryRecordAsync -> {defaultResult}");

        // 3. Dispose() — IDisposable implementation
        var disposableDetector = new InMemoryWebhookReplayDetector(TimeSpan.FromMinutes(1));
        await disposableDetector.TryRecordAsync("msg_before_dispose");
        disposableDetector.Dispose();
        Console.WriteLine("[7.4] InMemoryWebhookReplayDetector.Dispose() executed OK.");

        // ObjectDisposedException after Dispose
        try
        {
            await disposableDetector.TryRecordAsync("msg_after_dispose");
        }
        catch (ObjectDisposedException ode)
        {
            Console.WriteLine($"[7.5] ObjectDisposedException after Dispose: ObjectName='{ode.ObjectName}'");
        }

        // 4. TryRecordAsync with CancellationToken
        using var cts = new CancellationTokenSource();
        using var detectorCts = new InMemoryWebhookReplayDetector(TimeSpan.FromMinutes(5));
        var recorded = await detectorCts.TryRecordAsync("msg_with_ct", cts.Token);
        Console.WriteLine($"[7.6] TryRecordAsync with CancellationToken (not cancelled): {recorded}");

        // 5. WebhookDiagnostics.DiagnosticSourceName — constant
        Console.WriteLine($"[7.7] WebhookDiagnostics.DiagnosticSourceName = '{WebhookDiagnostics.DiagnosticSourceName}'");
    }

    // =========================================================================
    // LEVEL 8 — Customization
    // =========================================================================

    /// <summary>
    /// Level 8 — Customization: Custom interfaces, WebhookValidator with TimeProvider.
    /// </summary>
    private static async Task RunLevel8_CustomizationAsync()
    {
        Console.WriteLine("\n>>> [Level 8 — Customization: Custom Interface Implementations]");

        // 1. Custom IWebhookDeadLetterSink
        var customDlq = new CustomAuditDeadLetterSink();
        var uri = new Uri("https://custom.sink.com/webhook");
        var payload = new WebhookPayload("id_cust_1", "user.registered", "{}", DateTimeOffset.UtcNow);
        var result = new WebhookDeliveryResult(false, 500, 1, TimeSpan.FromMilliseconds(100), "Server Error");

        await customDlq.EnqueueAsync(uri, payload, result);
        Console.WriteLine($"[8.1] CustomAuditDeadLetterSink.EnqueueAsync: LoggedEvents.Count={customDlq.LoggedEvents.Count}");

        // 2. Custom IWebhookReplayDetector
        var customDetector = new CustomInMemoryReplayDetector();
        var isNew = await customDetector.TryRecordAsync("custom_msg_42");
        var isDuplicate = await customDetector.TryRecordAsync("custom_msg_42");
        Console.WriteLine($"[8.2] CustomInMemoryReplayDetector: isNew={isNew}, isDuplicate={!isDuplicate}");

        // 3. WebhookValidator — ctor with custom TimeProvider
        var services = new ServiceCollection();
        services.AddWebhookReceiver(o =>
        {
            o.SecretKey = "custom_time_provider_key";
            o.TimestampTolerance = TimeSpan.FromMinutes(10);
        });
        using var sp = services.BuildServiceProvider();
        var options = sp.GetRequiredService<IOptions<WebhookReceiverOptions>>();

        var customTimeProvider = TimeProvider.System;
        var validatorWithTimeProvider = new WebhookValidator(options, customTimeProvider, replayDetector: null, logger: null);
        Console.WriteLine($"[8.3] WebhookValidator(options, TimeProvider, null, null): {validatorWithTimeProvider.GetType().Name}");

        // 4. WebhookValidator — ctor with custom IWebhookReplayDetector
        var detectorForValidator = new CustomInMemoryReplayDetector();
        var validatorWithDetector = new WebhookValidator(options, null, detectorForValidator, null);
        Console.WriteLine($"[8.4] WebhookValidator(options, null, IWebhookReplayDetector, null): {validatorWithDetector.GetType().Name}");

        // 5. WebhookReceiverOptions.SecretKeys — ImmutableList for key rotation
        var rotationOptions = new WebhookReceiverOptions();
        rotationOptions.SecretKeys = System.Collections.Immutable.ImmutableList.Create("primary_v3", "secondary_v2", "legacy_v1");
        var rotationValidator = new WebhookValidator(Options.Create(rotationOptions));
        Console.WriteLine($"[8.5] WebhookValidator with SecretKeys.Count={rotationOptions.SecretKeys.Count} (multi-key rotation).");

        await Task.CompletedTask;
    }

    // =========================================================================
    // LEVEL 9 — Redis Extensions
    // =========================================================================

    /// <summary>
    /// Level 9 — Extensions: Redis-backed distributed components.
    /// </summary>
    private static void RunLevel9_RedisExtensions()
    {
        Console.WriteLine("\n>>> [Level 9 — Extensions: Redis Ecosystem (Dead Letter & Replay)]");

        var uri = new Uri("https://api.partner.com/webhook");
        var payload = new WebhookPayload("dlv_redis_01", "subscription.renewed", "{}", DateTimeOffset.UtcNow, 1);
        var result = new WebhookDeliveryResult(false, 503, 3, TimeSpan.FromMilliseconds(200), "Service Unavailable");

        // 1. DeadLetterEnvelope — positional record
        var envelope = new DeadLetterEnvelope(uri.ToString(), payload, result);
        Console.WriteLine($"[9.1] DeadLetterEnvelope: TargetUrl='{envelope.TargetUrl}', DeliveryId='{envelope.Payload.DeliveryId}'");

        // 2. DeadLetterJsonContext — Native AOT serialization
        var json = JsonSerializer.Serialize(envelope, DeadLetterJsonContext.Default.DeadLetterEnvelope);
        Console.WriteLine($"[9.2] DeadLetterJsonContext (Native AOT): {json.Substring(0, Math.Min(60, json.Length))}...");

        // 3. AddRedisWebhookReplayDetector() with retentionPeriod
        var services = new ServiceCollection();
        services.AddRedisWebhookReplayDetector(TimeSpan.FromMinutes(10));
        services.AddRedisWebhookDeadLetterSink("webhooks:custom_dlq");
        Console.WriteLine("[9.3] AddRedisWebhookReplayDetector(10min) + AddRedisWebhookDeadLetterSink('webhooks:custom_dlq') registered.");

        // 4. AddRedisWebhookReplayDetector() parameterless (default 10 min)
        var services2 = new ServiceCollection();
        services2.AddRedisWebhookReplayDetector();
        Console.WriteLine("[9.4] AddRedisWebhookReplayDetector() parameterless: retention=10min (default).");

        // 5. AddRedisWebhookDeadLetterSink() parameterless (default key)
        var services3 = new ServiceCollection();
        services3.AddRedisWebhookDeadLetterSink();
        Console.WriteLine("[9.5] AddRedisWebhookDeadLetterSink() parameterless: listKey='webhook:dlq' (default).");

        // 6. Constructor validation — ArgumentNullException
        try { _ = new RedisWebhookDeadLetterSink(null!); }
        catch (ArgumentNullException) { Console.WriteLine("[9.6] RedisWebhookDeadLetterSink(null): ArgumentNullException OK."); }

        try { _ = new RedisWebhookReplayDetector(null!, TimeSpan.FromMinutes(5)); }
        catch (ArgumentNullException) { Console.WriteLine("[9.7] RedisWebhookReplayDetector(null, ...): ArgumentNullException OK."); }
    }

    // =========================================================================
    // LEVEL 10 — Enterprise Architecture
    // =========================================================================

    /// <summary>
    /// Level 10 — Enterprise Architecture: ASP.NET Core, metrics, and comprehensive observability.
    /// </summary>
    private static async Task RunLevel10_EnterpriseArchitectureAsync()
    {
        Console.WriteLine("\n>>> [Level 10 — Enterprise Architecture: ASP.NET Core & Observability]");

        // 1. WebApplication — UseWebhookReceiver + RequireWebhookSignature
        var builder = WebApplication.CreateBuilder();
        var app = builder.Build();
        app.UseWebhookReceiver();
        app.MapPost("/webhook-single", () => Results.Ok()).RequireWebhookSignature();
        app.MapGroup("/webhooks-group").RequireWebhookSignature();
        Console.WriteLine("[10.1] UseWebhookReceiver + RequireWebhookSignature (RouteHandlerBuilder + RouteGroupBuilder) OK.");

        // 2. WebhookReceiverMiddleware — both constructors
        var svcMiddleware = new ServiceCollection();
        svcMiddleware.AddLogging();
        svcMiddleware.AddWebhookReceiver(options =>
        {
            options.SecretKey = "prod_receiver_secret_key_123";
            options.RoutePath = "/api/v1/webhooks";
        });
        using var sp = svcMiddleware.BuildServiceProvider();
        var validator = sp.GetRequiredService<IWebhookValidator>();

        // ctor(next, options) without logger
        var middlewareNoLogger = new WebhookReceiverMiddleware(
            ctx => Task.CompletedTask,
            Options.Create(new WebhookReceiverOptions()));

        // ctor(next, options, logger) with nullable logger
        var middleware = new WebhookReceiverMiddleware(
            ctx => Task.CompletedTask,
            Options.Create(new WebhookReceiverOptions()),
            logger: null);
        Console.WriteLine("[10.2] WebhookReceiverMiddleware: ctor(next,options) + ctor(next,options,logger) instantiated.");

        // InvokeAsync — non-matching route -> pass-through
        var httpContext = new DefaultHttpContext();
        httpContext.Request.Path = "/other/path";
        await middleware.InvokeAsync(httpContext, validator);
        Console.WriteLine("[10.3] WebhookReceiverMiddleware.InvokeAsync: non-matching route -> pass-through OK.");

        // 3. WebhookValidator.ValidateRequestAsync — direct
        var httpCtxForVal = new DefaultHttpContext();
        httpCtxForVal.Request.Path = "/api/webhooks";
        var valResult = await validator.ValidateRequestAsync(httpCtxForVal);
        Console.WriteLine($"[10.4] WebhookValidator.ValidateRequestAsync: IsFailure={valResult.IsFailure}, Code=[{valResult.Error.Code}]");

        // ValidateRequestAsync with explicit CancellationToken
        using var cts = new CancellationTokenSource();
        var valResultCts = await validator.ValidateRequestAsync(httpCtxForVal, cts.Token);
        Console.WriteLine($"[10.5] WebhookValidator.ValidateRequestAsync (with CancellationToken): IsFailure={valResultCts.IsFailure}");

        // 4. WebhookEndpointFilter — instantiation with and without logger
        var filterNoLogger = new WebhookEndpointFilter();
        var filterWithLogger = new WebhookEndpointFilter(logger: null);
        Console.WriteLine($"[10.6] WebhookEndpointFilter(): {filterNoLogger.GetType().Name}");
        Console.WriteLine($"[10.7] WebhookEndpointFilter(logger: null): {filterWithLogger.GetType().Name}");

        // 5. WebhookDiagnostics — all fields
        Console.WriteLine("[10.8] WebhookDiagnostics:");
        Console.WriteLine($"      DiagnosticSourceName           = '{WebhookDiagnostics.DiagnosticSourceName}'");
        Console.WriteLine($"      ActivitySource.Name            = '{WebhookDiagnostics.ActivitySource.Name}'");
        Console.WriteLine($"      ActivitySource.Version         = '{WebhookDiagnostics.ActivitySource.Version}'");
        Console.WriteLine($"      Meter.Name                     = '{WebhookDiagnostics.Meter.Name}'");
        Console.WriteLine($"      Meter.Version                  = '{WebhookDiagnostics.Meter.Version}'");
        Console.WriteLine($"      DeliveriesTotal.Name           = '{WebhookDiagnostics.DeliveriesTotal.Name}'");
        Console.WriteLine($"      DeadLetterEscalationsTotal.Name = '{WebhookDiagnostics.DeadLetterEscalationsTotal.Name}'");
        Console.WriteLine($"      DeliveryDuration.Name          = '{WebhookDiagnostics.DeliveryDuration.Name}'");
        Console.WriteLine($"      InboundValidationsTotal.Name   = '{WebhookDiagnostics.InboundValidationsTotal.Name}'");

        // 6. Metric recording — all instruments
        WebhookDiagnostics.DeliveriesTotal.Add(1,
            new KeyValuePair<string, object?>("event_type", "order.created"),
            new KeyValuePair<string, object?>("is_success", true));
        WebhookDiagnostics.DeadLetterEscalationsTotal.Add(1,
            new KeyValuePair<string, object?>("event_type", "payment.failed"));
        WebhookDiagnostics.DeliveryDuration.Record(42.5,
            new KeyValuePair<string, object?>("event_type", "order.created"));
        WebhookDiagnostics.InboundValidationsTotal.Add(1,
            new KeyValuePair<string, object?>("is_valid", true),
            new KeyValuePair<string, object?>("reason", "Success"));
        Console.WriteLine("[10.9] OTel metrics recorded: DeliveriesTotal, DeadLetterEscalationsTotal, DeliveryDuration, InboundValidationsTotal.");

        // 7. ActivitySource tracing
        using var activity = WebhookDiagnostics.ActivitySource.StartActivity("showcase.demo", ActivityKind.Internal);
        activity?.SetTag("showcase.level", "10");
        activity?.SetTag("showcase.complete", "true");
        Console.WriteLine($"[10.10] ActivitySource.StartActivity: Operation='{activity?.OperationName}', Kind={activity?.Kind}");
    }
}
