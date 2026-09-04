using System.Security.Cryptography;
using System.Text;
using System.IO.Compression;
using System.IO.Ports;

namespace NexusDisplay;

internal static class Program
{
    private static int Main()
    {
        ProductVersionRules();
        EnergyRules();
        PipelineIsolationRules();
        HandshakeRules();
        WindowsSerialRuntimeRules();
        SignatureRules();
        PackageExtractionRules();
        PublishedEnvelopeRules();
        Console.WriteLine("Desktop core tests passed.");
        return 0;
    }

    private static void ProductVersionRules()
    {
        Require(ProductVersion.TryParse("2.1.1", out ProductVersion development), "parse development");
        ProductVersion stable = ProductVersion.Parse("2.1.0");
        Require(stable > development, "stable channel follows development channels");
        Require(ProductVersion.Parse("2.1.9") < stable, "all development channels precede stable");
        Require(!ProductVersion.TryParse("2.01.1", out _), "reject leading zero");
        Require(!ProductVersion.TryParse("2.1.10", out _), "reject invalid channel");
        Require(!ProductVersion.TryParse("2.1.1-extra", out _), "reject trailing text");
        Require(development.IsInRange(ProductVersion.Parse("2.1.1"), 3), "range lower bound");
        Require(!ProductVersion.Parse("3.0.1").IsInRange(development, 3), "exclusive major");
    }

    private static void EnergyRules()
    {
        var energy = new EnergyAccumulator();
        energy.AddSample(0, 1000, 100.0);
        energy.AddSample(1000, 1000, 100.0);
        double first = energy.KilowattHours;
        Require(Math.Abs(first - 100.0 / 3_600_000.0) < 1e-12, "energy trapezoid");
        energy.AddSample(2000, 1000, -50.0);
        Require(energy.KilowattHours == first, "negative power cannot reduce energy");
        energy.AddSample(3000, 1000, double.NaN);
        Require(energy.KilowattHours == first, "NaN cannot change energy");
        energy.AddSample(30_000, 1000, 100.0);
        Require(energy.KilowattHours == first, "suspend-sized gap ignored");
    }

    private static void HandshakeRules()
    {
        const string nonce = "0123456789abcdef0123456789abcdef";
        string hello = ProtocolHandshake.CreateHello(nonce);
        Require(hello.Contains("\"protocol_major\":2", StringComparison.Ordinal), "hello protocol");
        string ready = $$"""
            {"type":"ready","product":"NEXUS_ESP32P4_DISPLAY","firmware_version":"2.1.1","protocol_major":2,"protocol_revision":0,"minimum_desktop_version":"2.1.1","maximum_desktop_major_exclusive":3,"nonce":"{{nonce}}"}
            """;
        FirmwareIdentity identity = ProtocolHandshake.ValidateReady(ready, nonce);
        Require(identity.Version == ProductVersion.Parse("2.1.1"), "ready version");
        RequireThrows<InvalidDataException>(() =>
            ProtocolHandshake.ValidateReady(ready.Replace("\"protocol_major\":2", "\"protocol_major\":2.5"), nonce),
            "fractional protocol rejected");
        RequireThrows<InvalidDataException>(() =>
            ProtocolHandshake.ValidateReady(ready.Replace(nonce, "ffffffffffffffffffffffffffffffff"), nonce),
            "nonce mismatch rejected");
        RequireThrows<InvalidDataException>(() =>
            ProtocolHandshake.ValidateReady(ready.Replace("\"type\":\"ready\"", "\"type\":\"ready\",\"type\":\"ready\""), nonce),
            "duplicate property rejected");
    }

    private static void PipelineIsolationRules()
    {
        using var samplerEntered = new ManualResetEventSlim(false);
        using var releaseSampler = new ManualResetEventSlim(false);
        int transports = 0;
        var initial = new TestSample(7);
        using var pipeline = new TelemetryPipeline<TestSample>(
            initial,
            () =>
            {
                samplerEntered.Set();
                releaseSampler.Wait();
                return new TestSample(8);
            },
            (sample, _) =>
            {
                Require(sample.Value is 7 or 8, "transport reads complete snapshot");
                Interlocked.Increment(ref transports);
            },
            exception => throw new InvalidOperationException("sample callback failed", exception),
            exception => throw new InvalidOperationException("transport callback failed", exception),
            TimeSpan.FromMilliseconds(10),
            TimeSpan.FromMilliseconds(10));

        pipeline.Start();
        Require(samplerEntered.Wait(TimeSpan.FromSeconds(1)), "sampler entered blocking provider");
        Thread.Sleep(120);
        Require(Volatile.Read(ref transports) >= 5,
            "transport remains live while hardware provider is blocked");
        releaseSampler.Set();
        PipelineStopResult stopped = pipeline.Stop(TimeSpan.FromSeconds(1));
        Require(stopped.TransportStopped && stopped.SamplingStopped, "pipeline stops cleanly");
    }

    private static void SignatureRules()
    {
        using ECDsa key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        byte[] payload = Encoding.UTF8.GetBytes("signed payload");
        byte[] signature = key.SignData(payload, HashAlgorithmName.SHA256,
            DSASignatureFormat.Rfc3279DerSequence);
        string publicKey = Convert.ToBase64String(key.ExportSubjectPublicKeyInfo());
        Require(SignatureVerifier.VerifyEcdsaP256(publicKey, payload, signature), "valid signature");
        payload[0] ^= 1;
        Require(!SignatureVerifier.VerifyEcdsaP256(publicKey, payload, signature), "tampering rejected");
        Require(!SignatureVerifier.VerifyEcdsaP256("invalid", payload, signature), "invalid key rejected");
    }

    private static void WindowsSerialRuntimeRules()
    {
        Require(OperatingSystem.IsWindows(), "desktop tests run on Windows");
        try
        {
            _ = SerialPort.GetPortNames();
        }
        catch (PlatformNotSupportedException exception)
        {
            throw new InvalidOperationException("FAIL: Windows serial runtime binding", exception);
        }
    }

    private static void PublishedEnvelopeRules()
    {
        string path = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,
            "..", "..", "..", "..", "package", "latest-update.json"));
        UpdatePayload payload = UpdateTrust.VerifyEnvelope(File.ReadAllText(path));
        Require(payload.Version == "2.1.2", "published envelope version");
        Require(payload.ProtocolMajor == 2 && payload.ProtocolRevision == 0,
            "published envelope protocol");
        string original = File.ReadAllText(path);
        int payloadIndex = original.IndexOf("\"payload\": \"", StringComparison.Ordinal) + 12;
        char replacement = original[payloadIndex] == 'A' ? 'B' : 'A';
        string tampered = original[..payloadIndex] + replacement + original[(payloadIndex + 1)..];
        RequireThrows<CryptographicException>(() => UpdateTrust.VerifyEnvelope(tampered),
            "published envelope tampering rejected");
    }

    private static void PackageExtractionRules()
    {
        string root = Path.Combine(Path.GetTempPath(), $"nexus-test-{Guid.NewGuid():N}");
        string archivePath = root + ".zip";
        Directory.CreateDirectory(root);
        try
        {
            using (ZipArchive archive = ZipFile.Open(archivePath, ZipArchiveMode.Create))
            {
                ZipArchiveEntry entry = archive.CreateEntry("../escape.txt");
                using StreamWriter writer = new(entry.Open(), new UTF8Encoding(false));
                writer.Write("blocked");
            }
            RequireThrows<InvalidDataException>(() =>
                UpdateTrust.ExtractValidatedPackage(archivePath, Path.Combine(root, "extract"),
                    ProductVersion.Parse("2.1.1")), "archive traversal rejected");
            Require(!File.Exists(Path.Combine(Path.GetDirectoryName(root)!, "escape.txt")),
                "archive cannot escape destination");
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
            if (File.Exists(archivePath)) File.Delete(archivePath);
        }
    }

    private static void Require(bool condition, string description)
    {
        if (!condition) throw new InvalidOperationException($"FAIL: {description}");
    }

    private static void RequireThrows<T>(Action action, string description) where T : Exception
    {
        try
        {
            action();
        }
        catch (T)
        {
            return;
        }
        throw new InvalidOperationException($"FAIL: {description}");
    }

    private sealed record TestSample(int Value);
}
