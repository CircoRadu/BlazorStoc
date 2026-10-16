using System.Diagnostics;
using BlazorStoc.Modules;
using BlazorStoc.Modules.Ocr;
using BlazorStoc.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace BlazorStoc.Checks;

// The OCR worker: the same results as the engine in-process, the answers of the engine as the same exceptions, and a worker that dies, hangs, is cancelled or
// writes garbage costing only that one job (circuit breaker of the "ocr" module, job folder removed, limits applied).
public static class OcrWorkerChecks
{
    private static string JobsRoot => Path.Combine(Path.GetTempPath(), "blazorstoc-ocr");
    private static int LeftoverJobs() => Directory.Exists(JobsRoot) ? Directory.GetDirectories(JobsRoot).Length : 0;

    private static IConfiguration Settings(params (string Key, string Value)[] values) =>
        new ConfigurationBuilder().AddInMemoryCollection(values.ToDictionary(item => item.Key, item => (string?)item.Value)).Build();

    private static ProcessStartInfo Cmd(string command) => new("cmd.exe") { ArgumentList = { "/c", command } };

    private static string ProjectRoot()
    {
        var root = AppContext.BaseDirectory;
        while (root is not null && !File.Exists(Path.Combine(root, "BlazorStoc.csproj"))) root = Path.GetDirectoryName(root.TrimEnd(Path.DirectorySeparatorChar));
        return root ?? throw new InvalidOperationException("project folder not found");
    }

    public static async Task RunAsync(Action<bool, string> check)
    {
        var tessdata = new TessdataPath(new TestWebHostEnvironment(ProjectRoot()));
        var before = LeftoverJobs();
        await EquivalenceAsync(check, tessdata);
        await AnswersAsync(check, tessdata);
        await FailuresAsync(check, tessdata);
        await LimitsAsync(check, tessdata);
        Wiring(check);
        check(LeftoverJobs() == before, "OCR worker: no job folder is left behind by any of the jobs above (success, refusal, crash, timeout, cancellation)");
    }

    // ---- the worker gives what the engine gives ----

    private static async Task EquivalenceAsync(Action<bool, string> check, IWebHostEnvironmentTessdataPath tessdata)
    {
        var modules = new ModuleRegistry();
        modules.Add(new OcrModule(), new ServiceCollection(), Settings());
        var runner = new OcrWorkerRunner(Settings());
        var worker = new OutOfProcessInvoicePdfReader(runner, modules, tessdata);
        var engine = new InvoicePdfReader(tessdata);

        var text = InvoiceFixtures.Make(new InvoiceSpec(Rows: 6)).Pdf;
        var expected = await engine.ReadAsync(new MemoryStream(text));
        var actual = await worker.ReadAsync(new MemoryStream(text));
        check(OcrJson.Write(actual.Document) == OcrJson.Write(expected.Document), "OCR worker: a PDF with a text layer is read to the same words as in-process");
        check(actual.PagePreviews.Count == expected.PagePreviews.Count && actual.PagePreviews.Zip(expected.PagePreviews).All(pair => pair.First.SequenceEqual(pair.Second)),
            "OCR worker: the page pictures come back byte for byte the same");
        check(actual.SourcePdf is not null && actual.SourcePdf.SequenceEqual(text), "OCR worker: the source file stays with the result (it is what the re-reading needs)");
        var same = await worker.RereadNumbersAsync(actual);
        check(ReferenceEquals(same, actual) && modules.StatusOf("ocr") == ModuleStatus.Healthy, "OCR worker: a text-layer document is not sent to a worker again for the numbers (no process)");

        var scan = InvoiceFixtures.Scan(text);
        // OCR_SAVE_SAMPLES=<folder> keeps the two generated invoices (a text one, a scanned one) to try the pages by hand.
        if (Environment.GetEnvironmentVariable("OCR_SAVE_SAMPLES") is { Length: > 0 } samples)
        {
            Directory.CreateDirectory(samples);
            File.WriteAllBytes(Path.Combine(samples, "factura-text.pdf"), text);
            File.WriteAllBytes(Path.Combine(samples, "factura-scanata.pdf"), scan);
        }
        var expectedScan = await engine.ReadAsync(new MemoryStream(scan));
        var actualScan = await worker.ReadAsync(new MemoryStream(scan));
        check(OcrJson.Write(actualScan.Document) == OcrJson.Write(expectedScan.Document) && actualScan.Document.Pages.Any(page => page.Source == InvoiceSources.Ocr),
            "OCR worker: a scanned invoice is recognised to the same words and rules as in-process");
        var expectedRead = await engine.RereadNumbersAsync(expectedScan);
        var actualRead = await worker.RereadNumbersAsync(actualScan);
        check(OcrJson.Write(actualRead.Document) == OcrJson.Write(expectedRead.Document), "OCR worker: the numbers read again cell by cell are the same as in-process");

        var inventoryPdf = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Fixtures", "inventar-proba.pdf"));
        using var inventoryEngine = new InventoryPickupOcrService(tessdata);
        var expectedInventory = await inventoryEngine.ScanAsync(new MemoryStream(inventoryPdf));
        var actualInventory = await new OutOfProcessInventoryPickupOcrService(runner, modules, tessdata).ScanAsync(new MemoryStream(inventoryPdf));
        check(OcrJson.Write(actualInventory) == OcrJson.Write(expectedInventory) && actualInventory.Rows.Count > 0, "OCR worker: the scanned inventory sheet gives the same rows as in-process");
        check(modules.StatusOf("ocr") == ModuleStatus.Healthy, "OCR worker: after real jobs the module is Healthy");

        var document = new InvoiceDocument([new InvoicePageData(1, 595, 842, "ocr", [new InvoiceWord(1, "Total", 10.5, 20.25, 30, 8, 3, true, 0.87)], [new InvoiceRule(true, 12.5, 1, 99)], 1.5, 90)]);
        var again = OcrJson.Read<InvoiceDocument>(OcrJson.Write(document));
        check(OcrJson.Write(again) == OcrJson.Write(document) && again.Pages[0].Words[0].Bold && again.Pages[0].Rules![0].Vertical && again.Pages[0].OcrTurn == 90 && again.Pages[0].Words[0].Confidence == 0.87,
            "OCR worker: the document survives the trip as JSON (words, rules, skew and turn)");
    }

    // ---- what the engine says to the user stays what it says ----

    private static async Task AnswersAsync(Action<bool, string> check, IWebHostEnvironmentTessdataPath tessdata)
    {
        var modules = new ModuleRegistry(new ManualTimeProvider());
        modules.Add(new OcrModule(), new ServiceCollection(), Settings());
        var runner = new OcrWorkerRunner(Settings());
        var invoices = new OutOfProcessInvoicePdfReader(runner, modules, tessdata);
        var notPdf = "this is not a pdf"u8.ToArray();
        var message = "";
        try { await invoices.ReadAsync(new MemoryStream(notPdf)); } catch (InvoiceAnalysisException exception) { message = exception.Message; }
        check(message == InvoiceAnalysisRules.NotAPdfMessage, "OCR worker: a file that is not a PDF is refused with the engine's own message");
        var empty = "";
        try { await invoices.ReadAsync(new MemoryStream()); } catch (InvoiceAnalysisException exception) { empty = exception.Message; }
        check(empty == InvoiceAnalysisRules.UnreadableMessage, "OCR worker: an empty file is refused with the engine's own message");
        var inventoryMessage = "";
        try { await new OutOfProcessInventoryPickupOcrService(runner, modules, tessdata).ScanAsync(new MemoryStream(notPdf)); }
        catch (InventoryPickupOcrException exception) { inventoryMessage = exception.Message; }
        check(inventoryMessage.Length > 0, "OCR worker: the inventory scan refuses a file that is not a PDF with its own exception");
        var tooLarge = false;
        try { await invoices.ReadAsync(new MemoryStream(new byte[InvoiceAnalysisRules.MaxFileSizeBytes + 1])); } catch (InvoiceAnalysisException exception) { tooLarge = exception.Message == InvoiceAnalysisRules.TooLargeMessage; }
        check(tooLarge, "OCR worker: a file over the size limit is refused without starting a process");
        for (var i = 0; i < 6; i++) { try { await invoices.ReadAsync(new MemoryStream(notPdf)); } catch (InvoiceAnalysisException) { } }
        check(modules.StatusOf("ocr") == ModuleStatus.Healthy, "OCR worker: refused files (answers of the engine) never count as failures of the module");
    }

    // ---- a worker that fails costs one job ----

    private static async Task FailuresAsync(Action<bool, string> check, IWebHostEnvironmentTessdataPath tessdata)
    {
        var clock = new ManualTimeProvider();
        var modules = new ModuleRegistry(clock);
        modules.Add(new OcrModule(), new ServiceCollection(), Settings());
        var starts = 0;
        var good = new OcrWorkerRunner(Settings());
        var crashing = new OcrWorkerRunner(Settings(), launch: _ => { starts++; return Cmd("exit 3"); });
        var pdf = InvoiceFixtures.Make(new InvoiceSpec(Rows: 3)).Pdf;

        var message = "";
        try { await new OutOfProcessInvoicePdfReader(crashing, modules, tessdata).ReadAsync(new MemoryStream(pdf)); } catch (InvoiceAnalysisException exception) { message = exception.Message; }
        check(message == OcrRules.CrashedMessage && starts == 1 && modules.StatusOf("ocr") == ModuleStatus.Degraded,
            "OCR worker: a worker that dies without a result gives the pages a message and makes the module Degraded");
        for (var i = 0; i < ModuleRegistry.FailureThreshold - 1; i++) { try { await new OutOfProcessInvoicePdfReader(crashing, modules, tessdata).ReadAsync(new MemoryStream(pdf)); } catch (InvoiceAnalysisException) { } }
        check(modules.StatusOf("ocr") == ModuleStatus.Disabled && starts == ModuleRegistry.FailureThreshold, "OCR worker: repeated crashes take the OCR module out");
        var unavailable = "";
        try { await new OutOfProcessInvoicePdfReader(crashing, modules, tessdata).ReadAsync(new MemoryStream(pdf)); } catch (InvoiceAnalysisException exception) { unavailable = exception.Message; }
        check(unavailable == OcrRules.UnavailableMessage && starts == ModuleRegistry.FailureThreshold, "OCR worker: with the circuit open the pages are told so and no process is started");
        var inventoryUnavailable = "";
        try { await new OutOfProcessInventoryPickupOcrService(crashing, modules, tessdata).ScanAsync(new MemoryStream(pdf)); } catch (InventoryPickupOcrException exception) { inventoryUnavailable = exception.Message; }
        check(inventoryUnavailable == OcrRules.UnavailableMessage, "OCR worker: the inventory scan is told the same while the circuit is open");
        clock.Advance(ModuleRegistry.Cooldown + TimeSpan.FromSeconds(1));
        var recovered = await new OutOfProcessInvoicePdfReader(good, modules, tessdata).ReadAsync(new MemoryStream(pdf));
        check(recovered.Document.Pages.Count == 1 && modules.StatusOf("ocr") == ModuleStatus.Healthy, "OCR worker: after the cooldown a good job brings the module back with no restart of the application");

        var garbage = new OcrWorkerRunner(Settings(), launch: folder => new ProcessStartInfo("powershell.exe")
            { ArgumentList = { "-NoProfile", "-Command", $"Set-Content -Path '{Path.Combine(folder, OcrFiles.Result)}' -Value 'not json at all'" } });
        var garbageMessage = "";
        try { await new OutOfProcessInvoicePdfReader(garbage, modules, tessdata).ReadAsync(new MemoryStream(pdf)); } catch (InvoiceAnalysisException exception) { garbageMessage = exception.Message; }
        check(garbageMessage == OcrRules.CrashedMessage, "OCR worker: a result that is not valid JSON is a failure of the job, not a crash of the application");
        var missing = new OcrWorkerRunner(Settings(), launch: _ => new ProcessStartInfo("this-program-does-not-exist.exe"));
        var missingMessage = "";
        try { await new OutOfProcessInvoicePdfReader(missing, modules, tessdata).ReadAsync(new MemoryStream(pdf)); } catch (InvoiceAnalysisException exception) { missingMessage = exception.Message; }
        check(missingMessage == OcrRules.CrashedMessage, "OCR worker: a worker that cannot even be started is a failure of the job");
        modules.ReportSuccess("ocr");

        var hanging = new OcrWorkerRunner(Settings(("Ocr:TimeoutSeconds", "5")), launch: _ => Cmd("ping -n 60 127.0.0.1 > nul"));
        var watch = Stopwatch.StartNew();
        var timeoutMessage = "";
        try { await new OutOfProcessInvoicePdfReader(hanging, modules, tessdata).ReadAsync(new MemoryStream(pdf)); } catch (InvoiceAnalysisException exception) { timeoutMessage = exception.Message; }
        check(timeoutMessage == OcrRules.TimeoutMessage && watch.Elapsed < TimeSpan.FromSeconds(25), $"OCR worker: a worker over the time limit is stopped and the page gets the timeout message ({watch.Elapsed.TotalSeconds:0.0} s)");

        using var cancel = new CancellationTokenSource(TimeSpan.FromSeconds(1));
        var cancelledRunner = new OcrWorkerRunner(Settings(), launch: _ => Cmd("ping -n 60 127.0.0.1 > nul"));
        var cancelled = false;
        watch.Restart();
        try { await new OutOfProcessInvoicePdfReader(cancelledRunner, modules, tessdata).ReadAsync(new MemoryStream(pdf), cancel.Token); } catch (OperationCanceledException) { cancelled = true; }
        check(cancelled && watch.Elapsed < TimeSpan.FromSeconds(20), $"OCR worker: cancelling the request (the user leaves the page) stops the worker ({watch.Elapsed.TotalSeconds:0.0} s)");
        var statusAfter = modules.StatusOf("ocr");
        check(statusAfter != ModuleStatus.Disabled, "OCR worker: a cancelled request is not a failure of the module");
    }

    // ---- limits ----

    private static async Task LimitsAsync(Action<bool, string> check, IWebHostEnvironmentTessdataPath tessdata)
    {
        var modules = new ModuleRegistry(new ManualTimeProvider());
        modules.Add(new OcrModule(), new ServiceCollection(), Settings());
        var pdf = InvoiceFixtures.Make(new InvoiceSpec(Rows: 2)).Pdf;

        ProcessStartInfo? seen = null;
        var capture = new OcrWorkerRunner(Settings(("Ocr:MaxMemoryMb", "1024")), launch: folder => { seen = Cmd("exit 0"); return seen; });
        try { await new OutOfProcessInvoicePdfReader(capture, modules, tessdata).ReadAsync(new MemoryStream(pdf)); } catch (InvoiceAnalysisException) { }
        check(seen?.Environment["DOTNET_GCHeapHardLimit"] == "40000000", "OCR worker: the worker gets a heap limit from Ocr:MaxMemoryMb (1024 MB)");
        check(seen is { RedirectStandardError: true, CreateNoWindow: true, UseShellExecute: false }, "OCR worker: the worker runs without a window and with its output read by the application");

        var running = 0;
        var most = 0;
        var gate = new object();
        var first = new ManualResetEventSlim(false);
        var firstStarted = new ManualResetEventSlim(false);
        var serial = new OcrWorkerRunner(Settings(("Ocr:MaxParallel", "1")), launch: _ =>
        {
            lock (gate) { running++; most = Math.Max(most, running); }
            firstStarted.Set();
            first.Wait(TimeSpan.FromSeconds(10));
            lock (gate) running--;
            return Cmd("exit 0");
        });
        var reader = new OutOfProcessInvoicePdfReader(serial, modules, tessdata);
        var one = Task.Run(async () => { try { await reader.ReadAsync(new MemoryStream(pdf)); } catch (InvoiceAnalysisException) { } });
        firstStarted.Wait(TimeSpan.FromSeconds(10));
        var two = Task.Run(async () => { try { await reader.ReadAsync(new MemoryStream(pdf)); } catch (InvoiceAnalysisException) { } });
        await Task.Delay(700);
        bool secondWaiting;
        lock (gate) secondWaiting = running == 1 && most == 1;
        first.Set();
        await Task.WhenAll(one, two);
        check(secondWaiting && most == 1, "OCR worker: with Ocr:MaxParallel=1 the second job waits for the first (no two workers at once)");

        // (a registry of its own: the jobs above end without a result on purpose and count as failures)
        modules = new ModuleRegistry(new ManualTimeProvider());
        modules.Add(new OcrModule(), new ServiceCollection(), Settings());
        var cancelWhileWaiting = new OcrWorkerRunner(Settings(("Ocr:MaxParallel", "1")), launch: _ => { Thread.Sleep(1500); return Cmd("exit 0"); });
        var holder = Task.Run(async () => { try { await new OutOfProcessInvoicePdfReader(cancelWhileWaiting, modules, tessdata).ReadAsync(new MemoryStream(pdf)); } catch (InvoiceAnalysisException) { } });
        await Task.Delay(300);
        using var waitingCancel = new CancellationTokenSource(TimeSpan.FromMilliseconds(300));
        var waitingCancelled = false;
        try { await new OutOfProcessInvoicePdfReader(cancelWhileWaiting, modules, tessdata).ReadAsync(new MemoryStream(pdf), waitingCancel.Token); } catch (OperationCanceledException) { waitingCancelled = true; }
        await holder;
        check(waitingCancelled, "OCR worker: a request waiting for a free worker can be cancelled");
    }

    // ---- registration ----

    private static void Wiring(Action<bool, string> check)
    {
        ServiceProvider Build(params (string Key, string Value)[] values)
        {
            var configuration = Settings(values);
            var services = new ServiceCollection();
            services.AddLogging();
            services.AddSingleton(configuration);
            services.AddSingleton<IWebHostEnvironmentTessdataPath>(new FixedPath("x"));
            var registry = new ModuleRegistry();
            services.AddSingleton(registry);
            registry.Add(new OcrModule(), services, configuration);
            return services.BuildServiceProvider();
        }
        using (var worker = Build())
            check(worker.GetRequiredService<IInvoicePdfReader>().GetType().Name == "OutOfProcessInvoicePdfReader" && worker.GetRequiredService<IInvoicePdfReader>() is IInvoiceNumberRereader
                && worker.GetRequiredService<IInventoryPickupOcrService>().GetType().Name == "OutOfProcessInventoryPickupOcrService",
                "OCR worker: by default the readers the pages use run in a worker process (the re-reading too)");
        using (var inProcess = Build(("Ocr:Mode", "InProcess")))
            check(inProcess.GetRequiredService<IInvoicePdfReader>() is InvoicePdfReader && inProcess.GetRequiredService<IInventoryPickupOcrService>() is InventoryPickupOcrService,
                "OCR worker: Ocr:Mode=InProcess keeps the engine inside the application (the way back)");
        using (var off = Build(("Modules:ocr:Enabled", "false")))
        {
            var reader = off.GetRequiredService<IInvoicePdfReader>();
            var told = "";
            try { reader.ReadAsync(new MemoryStream()).GetAwaiter().GetResult(); } catch (InvoiceAnalysisException exception) { told = exception.Message; }
            var inventoryTold = "";
            try { off.GetRequiredService<IInventoryPickupOcrService>().ScanAsync(new MemoryStream()).GetAwaiter().GetResult(); } catch (InventoryPickupOcrException exception) { inventoryTold = exception.Message; }
            check(told == OcrRules.UnavailableMessage && inventoryTold == OcrRules.UnavailableMessage,
                "OCR worker: with the module switched off the readers still resolve (the pages are created) and say that recognition is unavailable");
        }
        // The registrations of the real application, in the order of Program.cs (the parts that touch no files or static state): a registration made later by
        // another method would silently replace the module's, and the pages would keep reading in-process.
        {
            var configuration = Settings();
            var registry = new ModuleRegistry();
            var services = new ServiceCollection();
            BlazorStoc.Startup.ServiceRegistration.AddCoreServices(services);
            BlazorStoc.Startup.ServiceRegistration.AddModules(services, configuration, registry);
            BlazorStoc.Startup.ServiceRegistration.AddUserServices(services);
            BlazorStoc.Startup.ServiceRegistration.AddStockServices(services);
            BlazorStoc.Startup.ServiceRegistration.AddProjectServices(services);
            check(services.Count(descriptor => descriptor.ServiceType == typeof(IInvoicePdfReader)) == 1 && services.Count(descriptor => descriptor.ServiceType == typeof(IInventoryPickupOcrService)) == 1,
                "OCR worker: in the registrations of the application the readers are registered once, by the module (no later registration replaces them)");
            var last = services.Last(descriptor => descriptor.ServiceType == typeof(IInvoicePdfReader));
            check(last.ImplementationFactory is not null && last.ImplementationType is null, "OCR worker: the invoice reader of the application is the module's factory, not the in-process class");
        }
        check(new OcrModule().Nav.Count == 0 && new OcrModule().Id == "ocr", "OCR worker: the module has no page and no menu entry of its own");
        check(ModuleRegistry.IsFault(new OcrWorkerException("x")) && !ModuleRegistry.IsFault(new InvoiceAnalysisException("x")) && !ModuleRegistry.IsFault(new InventoryPickupOcrException("x")),
            "OCR worker: a dead worker is a failure of the module, a refused file is not");
    }

    private sealed class FixedPath(string path) : IWebHostEnvironmentTessdataPath
    {
        public string Path { get; } = path;
    }
}
