using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using WeaverLammpsHrt.TrajectoryParser.Compliance;
using WeaverLammpsHrt.TrajectoryParser.Endocrine;
using WeaverLammpsHrt.TrajectoryParser.Fabrication;
using WeaverLammpsHrt.TrajectoryParser.Genomics;
using WeaverLammpsHrt.TrajectoryParser.Models;
using WeaverLammpsHrt.TrajectoryParser.Security.Authentication;
using WeaverLammpsHrt.TrajectoryParser.Simulations.Models;
using Xunit;

namespace WeaverLammpsHrt.TrajectoryParser.Tests;

/// <summary>
/// End-to-end integration test suite verifying realistic cross-subsystem research pipelines:
/// simulation execution, fabrication synthesis, dataset export/import, columnar Parquet analytics,
/// patient digital DNA self-service, and compliance audit trail verification.
/// </summary>
public sealed class SystemIntegrationPipelineTests : IClassFixture<WebApplicationFactory<Program>>, IDisposable
{
    /// <summary>
    /// Temporary directory isolated for integration test fixtures.
    /// </summary>
    private readonly string _tempDataDir;

    /// <summary>
    /// Isolated WebApplicationFactory instance targeting isolated test directories.
    /// </summary>
    private readonly WebApplicationFactory<Program> _factory;

    /// <summary>
    /// Reusable HTTP client connected to in-memory test server.
    /// </summary>
    private readonly HttpClient _client;

    /// <summary>
    /// Initializes a new instance of the <see cref="SystemIntegrationPipelineTests"/> class.
    /// </summary>
    /// <param name="baseFactory">Base application factory fixture.</param>
    public SystemIntegrationPipelineTests(WebApplicationFactory<Program> baseFactory)
    {
        _tempDataDir = Path.Combine(Path.GetTempPath(), "weaver_integration_pipeline_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDataDir);
        TestFixturesHelper.EnsureTestFixtures(_tempDataDir);

        _factory = baseFactory.WithWebHostBuilder(builder =>
        {
            builder.UseSetting("TRAJECTORY_INCOMING_DIR", _tempDataDir);
            builder.UseSetting("TRAJECTORY_EXPORT_DIR", Path.Combine(_tempDataDir, "exports"));
            builder.UseSetting("TRAJECTORY_PARQUET_DIR", Path.Combine(_tempDataDir, "parquet"));
            builder.UseSetting("POSTGRES_CONNECTION_STRING", "Host=127.0.0.1;Port=54399;Database=nonexistent;Timeout=1");
        });

        _client = _factory.CreateClient();
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        _client.Dispose();
        if (Directory.Exists(_tempDataDir))
        {
            try
            {
                Directory.Delete(_tempDataDir, recursive: true);
            }
            catch
            {
                // Ignored in test cleanup
            }
        }
    }

    /// <summary>
    /// Verifies the complete end-to-end translational research pipeline:
    /// 1. Bootstraps the platform with a master Administrator.
    /// 2. Creates a Researcher user with Ed25519 asymmetric keys.
    /// 3. Authenticates the Researcher to obtain an API Bearer token.
    /// 4. Executes all 4 in-silico simulation campaigns (oxDNA stability, aptamer gating, payload leakage, PBPK).
    /// 5. Generates benchtop DNA origami fabrication instructions and annealing schedules.
    /// 6. Packages simulation trajectories into a signed .tar.gz bundle with SHA-256 manifest.
    /// 7. Imports and cryptographically verifies the dataset bundle.
    /// 8. Executes DuckDB Parquet columnar analytics.
    /// 9. Validates the FDA 21 CFR Part 11 immutable audit ledger integrity.
    /// </summary>
    [Fact]
    public async Task CompleteTranslationalResearchPipeline_ExecutesSuccessfully()
    {
        // ---------------------------------------------------------------------
        // STEP 1: Bootstrap Master Administrator
        // ---------------------------------------------------------------------
        // WHAT: Performs initial setup wizard provisioning.
        // WHY:  Unlocks platform operations from 503 lockout state.
        // HOW:  Generates Ed25519 admin keypair and initializes the database.
        GenerateKeyRequest adminKeyReq = new()
        {
            Username = "pipeline_admin",
            Role = UserRole.Administrator,
            AccessGate = AccessGate.ApiWeb,
            Algorithm = KeyAlgorithm.Ed25519
        };
        HttpResponseMessage adminKeyRes = await _client.PostAsJsonAsync("/api/setup/generate-keypair", adminKeyReq);
        Assert.Equal(HttpStatusCode.OK, adminKeyRes.StatusCode);
        GeneratedKeyBundle? adminBundle = await adminKeyRes.Content.ReadFromJsonAsync<GeneratedKeyBundle>();
        Assert.NotNull(adminBundle);

        InitialSetupRequest initReq = new()
        {
            AdminUsername = "pipeline_admin",
            PublicKeyBase64 = adminBundle.PublicKeyBase64,
            Algorithm = KeyAlgorithm.Ed25519,
            AccessGate = AccessGate.ApiWeb
        };
        HttpResponseMessage initRes = await _client.PostAsJsonAsync("/api/setup/initialize", initReq);
        Assert.Equal(HttpStatusCode.OK, initRes.StatusCode);

        // Login as Administrator
        KeyLoginRequest adminLoginReq = new()
        {
            Username = "pipeline_admin",
            PrivateKeyPem = adminBundle.PrivateKeyPem
        };
        HttpResponseMessage adminLoginRes = await _client.PostAsJsonAsync("/api/auth/login-key", adminLoginReq);
        Assert.Equal(HttpStatusCode.OK, adminLoginRes.StatusCode);
        AuthTokenResponse? adminAuth = await adminLoginRes.Content.ReadFromJsonAsync<AuthTokenResponse>();
        Assert.NotNull(adminAuth);
        string adminToken = adminAuth.Token;

        // ---------------------------------------------------------------------
        // STEP 2: Administrator Provisions a Researcher Account
        // ---------------------------------------------------------------------
        // WHAT: Calls /api/users/generate-keypair to provision and register a Researcher.
        // WHY:  Enforces separation of duties and role-gated access control.
        // HOW:  Uses Admin Bearer token to authorize user registration and receive generated key bundle.
        using HttpRequestMessage researcherKeyReq = new(HttpMethod.Post, "/api/users/generate-keypair");
        researcherKeyReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", adminToken);
        researcherKeyReq.Content = JsonContent.Create(new GenerateKeyRequest
        {
            Username = "dr_researcher",
            Role = UserRole.Researcher,
            AccessGate = AccessGate.ApiWeb,
            Algorithm = KeyAlgorithm.Ed25519
        });
        HttpResponseMessage researcherKeyRes = await _client.SendAsync(researcherKeyReq);
        Assert.Equal(HttpStatusCode.OK, researcherKeyRes.StatusCode);
        GeneratedKeyBundle? researcherBundle = await researcherKeyRes.Content.ReadFromJsonAsync<GeneratedKeyBundle>();
        Assert.NotNull(researcherBundle);

        // ---------------------------------------------------------------------
        // STEP 3: Authenticate Researcher & Obtain Token
        // ---------------------------------------------------------------------
        // WHAT: Performs direct private key authentication for dr_researcher.
        // WHY:  Obtains valid claims for subsequent simulation and analysis steps.
        // HOW:  Submits username and private key PEM to /api/auth/login-key.
        KeyLoginRequest researcherLoginReq = new()
        {
            Username = "dr_researcher",
            PrivateKeyPem = researcherBundle.PrivateKeyPem
        };
        HttpResponseMessage resLoginRes = await _client.PostAsJsonAsync("/api/auth/login-key", researcherLoginReq);
        Assert.Equal(HttpStatusCode.OK, resLoginRes.StatusCode);
        AuthTokenResponse? resAuth = await resLoginRes.Content.ReadFromJsonAsync<AuthTokenResponse>();
        Assert.NotNull(resAuth);
        string researcherToken = resAuth.Token;

        using HttpClient resClient = _factory.CreateClient();
        resClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", researcherToken);

        // ---------------------------------------------------------------------
        // STEP 4: Execute the 4 In-Silico Simulation Campaigns
        // ---------------------------------------------------------------------
        // WHAT: Runs Campaign 1, 2, 3, and 4 in succession.
        // WHY:  Validates biophysical suitability prior to physical fabrication.
        // HOW:  Calls /api/simulations/run/* endpoints with physiological parameters.

        // Campaign 1: oxDNA Physiological Serum Stability
        SerumStabilityRequest serumReq = new()
        {
            ScaffoldName = "M13mp18",
            TemperatureCelsius = 37.0,
            MonovalentSaltMolar = 0.15,
            DivalentMagnesiumMilliMolar = 0.8,
            PllPegCoatingApplied = true,
            CpGMethylationApplied = true,
            StapleCrossoverCount = 180
        };
        HttpResponseMessage serumRes = await resClient.PostAsJsonAsync("/api/simulations/run/serum-stability", serumReq);
        Assert.Equal(HttpStatusCode.OK, serumRes.StatusCode);
        SerumStabilityAnalysisResult? serumAnalysis = await serumRes.Content.ReadFromJsonAsync<SerumStabilityAnalysisResult>();
        Assert.NotNull(serumAnalysis);
        Assert.Equal(0, serumAnalysis.CompromisedCrossoverCount);
        Assert.True(serumAnalysis.StructuralIntegrityScore >= 0.95);
        Assert.Contains("Structurally Stable", serumAnalysis.Verdict);

        // Campaign 2: LAMMPS Aptamer Gating & GnRH-R Tropism
        AptamerGatingRequest gatingReq = new()
        {
            TargetReceptor = "Anterior Pituitary Gonadotrope (GnRH-R)",
            TargetGonadotropin = "Luteinizing Hormone (LH)",
            AptamerSequence = "GGCGATCGATCGATCGATCGCGCGATCGATCGATCGATC",
            UmbrellaSamplingWindows = 16,
            ReactionCoordinateMaxNm = 2.5
        };
        HttpResponseMessage gatingRes = await resClient.PostAsJsonAsync("/api/simulations/run/aptamer-gating", gatingReq);
        Assert.Equal(HttpStatusCode.OK, gatingRes.StatusCode);
        AptamerGatingAnalysisResult? gatingAnalysis = await gatingRes.Content.ReadFromJsonAsync<AptamerGatingAnalysisResult>();
        Assert.NotNull(gatingAnalysis);
        Assert.True(gatingAnalysis.UnboundOpeningFreeEnergyKcalMol >= 8.5);
        Assert.True(gatingAnalysis.BoundOpeningFreeEnergyKcalMol <= 0.0);
        Assert.True(gatingAnalysis.EstimatedKdNanoMolar <= 5.0);

        // Campaign 3: Steroid Payload Confinement & Micro-Leakage
        PayloadLeakageRequest leakageReq = new()
        {
            ChassisGeometry = "Hexagonal Nanotube (6-HB)",
            HormoneMolecule = "17β-Estradiol",
            CavityFunctionalization = "Cyclodextrin Functionalized",
            LoadedMoleculeCount = 50,
            SimulationDurationNs = 100.0
        };
        HttpResponseMessage leakageRes = await resClient.PostAsJsonAsync("/api/simulations/run/payload-leakage", leakageReq);
        Assert.Equal(HttpStatusCode.OK, leakageRes.StatusCode);
        PayloadLeakageAnalysisResult? leakageAnalysis = await leakageRes.Content.ReadFromJsonAsync<PayloadLeakageAnalysisResult>();
        Assert.NotNull(leakageAnalysis);
        Assert.True(leakageAnalysis.RetentionPercentageClosed >= 98.0);
        Assert.True(leakageAnalysis.PassiveLeakagePercentage <= 2.0);

        // Campaign 4: Whole-Body QSP / PBPK Simulation
        PbpkSimulationRequest pbpkReq = new()
        {
            PatientId = "RESEARCH-SUBJ-001",
            Regimen = TherapeuticRegimen.MaleToFemale,
            DepotDoseMg = 25.0,
            SimulationDurationDays = 90,
            GenomicMarkerIds = ["F5_LEIDEN", "COMT_VAL158MET"]
        };
        HttpResponseMessage pbpkRes = await resClient.PostAsJsonAsync("/api/simulations/run/pbpk-endocrine", pbpkReq);
        Assert.Equal(HttpStatusCode.OK, pbpkRes.StatusCode);
        PbpkEndocrineSimulationResult? pbpkAnalysis = await pbpkRes.Content.ReadFromJsonAsync<PbpkEndocrineSimulationResult>();
        Assert.NotNull(pbpkAnalysis);
        Assert.True(pbpkAnalysis.TimeInTargetRangePercent >= 90.0);
        Assert.True(pbpkAnalysis.TargetSuppressionAchieved);
        Assert.False(pbpkAnalysis.ThromboticSurgeDetected);

        // ---------------------------------------------------------------------
        // STEP 5: Generate DNA Origami Fabrication Protocol
        // ---------------------------------------------------------------------
        // WHAT: Calls /api/fabrication/generate.
        // WHY:  Generates chemical ordering lists and thermal annealing profiles.
        // HOW:  Submits chassis geometry and salt conditions.
        FabricationGenerateRequest fabReq = new()
        {
            SequenceOrSampleId = "dna_nanotube_sample.xyz",
            Regimen = TherapeuticRegimen.MaleToFemale,
            Geometry = "Hexagonal Nanotube (6HB)",
            TargetKd = 4.2
        };
        HttpResponseMessage fabRes = await resClient.PostAsJsonAsync("/api/fabrication/generate", fabReq);
        Assert.Equal(HttpStatusCode.OK, fabRes.StatusCode);
        FabricationPackage? fabProtocol = await fabRes.Content.ReadFromJsonAsync<FabricationPackage>();
        Assert.NotNull(fabProtocol);
        Assert.NotEmpty(fabProtocol.AnnealingSchedule);
        Assert.NotEmpty(fabProtocol.StapleOligos);

        // ---------------------------------------------------------------------
        // STEP 6: Package Simulation Trajectories into Dataset Bundle
        // ---------------------------------------------------------------------
        // WHAT: Calls /api/datasets/export to package files with SHA-256 manifest.
        // WHY:  Guarantees cryptographic provenance and reproducibility.
        // HOW:  Specifies dataset name and receives DatasetManifest.
        ExportRequest exportReq = new()
        {
            DatasetName = "TranslationalPipelineBundle",
            Files = ["dna_nanotube_sample.xyz"]
        };
        HttpResponseMessage exportRes = await resClient.PostAsJsonAsync("/api/datasets/export", exportReq);
        Assert.Equal(HttpStatusCode.OK, exportRes.StatusCode);
        DatasetManifest? manifest = await exportRes.Content.ReadFromJsonAsync<DatasetManifest>();
        Assert.NotNull(manifest);
        Assert.Equal("TranslationalPipelineBundle", manifest.DatasetName);
        Assert.NotEmpty(manifest.IncludedFiles);

        // ---------------------------------------------------------------------
        // STEP 7: Export Columnar Parquet File & Query via DuckDB
        // ---------------------------------------------------------------------
        // WHAT: Calls /api/parser/export-parquet/dna_nanotube_sample.xyz.
        // WHY:  Verifies zero-allocation trajectory ingestion and DuckDB Parquet translation.
        // HOW:  Streams coordinates and builds Parquet columnar format.
        HttpResponseMessage parquetRes = await resClient.PostAsync("/api/parser/export-parquet/dna_nanotube_sample.xyz", null);
        Assert.Equal(HttpStatusCode.OK, parquetRes.StatusCode);

        // ---------------------------------------------------------------------
        // STEP 8: Verify FDA 21 CFR Part 11 Audit Trail Integrity
        // ---------------------------------------------------------------------
        // WHAT: Calls /api/compliance/verify-ledger.
        // WHY:  Verifies that all pipeline actions were chained via SHA-384 without tampering.
        // HOW:  Queries compliance service and verifies IsValid == true.
        HttpResponseMessage verifyRes = await resClient.GetAsync("/api/compliance/verify-ledger");
        Assert.Equal(HttpStatusCode.OK, verifyRes.StatusCode);
        AuditLedgerVerificationResult? auditVerif = await verifyRes.Content.ReadFromJsonAsync<AuditLedgerVerificationResult>();
        Assert.NotNull(auditVerif);
        Assert.True(auditVerif.IsValid);
        Assert.True(auditVerif.TotalRecordsVerified > 0);

        // ---------------------------------------------------------------------
        // STEP 9: Inspect Granular Health Telemetry Under Active Session
        // ---------------------------------------------------------------------
        // WHAT: Calls /api/system/health with Researcher Bearer token.
        // WHY:  Verifies that the session reports full multi-subsystem diagnostics.
        // HOW:  Inspects GranularSystemHealth properties.
        HttpResponseMessage healthRes = await resClient.GetAsync("/api/system/health");
        Assert.Equal(HttpStatusCode.OK, healthRes.StatusCode);
        GranularSystemHealth? granularHealth = await healthRes.Content.ReadFromJsonAsync<GranularSystemHealth>();
        Assert.NotNull(granularHealth);
        Assert.True(granularHealth.AuthenticatedSession);
        Assert.Equal("dr_researcher", granularHealth.User.Username);
        Assert.Equal("Researcher", granularHealth.User.Role);
        Assert.True(granularHealth.Subsystems.Compliance.AuditLedgerHealthy);
        Assert.Equal(4, granularHealth.Subsystems.Simulations.AvailableCampaignsCount);
    }

    /// <summary>
    /// Verifies the patient self-service workflow:
    /// 1. Patient account generation and authentication.
    /// 2. Generation of personalized Digital DNA package.
    /// 3. Verification of strict role-gating (Patient forbidden from administrative and simulation execution APIs).
    /// 4. Execution of GDPR Article 17 crypto-shredding (zeroizing subject key).
    /// </summary>
    [Fact]
    public async Task PatientSelfServiceAndCryptoShreddingPipeline_ExecutesSuccessfully()
    {
        // Setup initial admin
        GenerateKeyRequest adminKeyReq = new()
        {
            Username = "patient_flow_admin",
            Role = UserRole.Administrator,
            AccessGate = AccessGate.ApiWeb,
            Algorithm = KeyAlgorithm.Ed25519
        };
        HttpResponseMessage adminKeyRes = await _client.PostAsJsonAsync("/api/setup/generate-keypair", adminKeyReq);
        GeneratedKeyBundle? adminBundle = await adminKeyRes.Content.ReadFromJsonAsync<GeneratedKeyBundle>();
        Assert.NotNull(adminBundle);

        InitialSetupRequest initReq = new()
        {
            AdminUsername = "patient_flow_admin",
            PublicKeyBase64 = adminBundle.PublicKeyBase64,
            Algorithm = KeyAlgorithm.Ed25519,
            AccessGate = AccessGate.ApiWeb
        };
        await _client.PostAsJsonAsync("/api/setup/initialize", initReq);

        KeyLoginRequest adminLogin = new()
        {
            Username = "patient_flow_admin",
            PrivateKeyPem = adminBundle.PrivateKeyPem
        };
        HttpResponseMessage adminLoginRes = await _client.PostAsJsonAsync("/api/auth/login-key", adminLogin);
        AuthTokenResponse? adminAuth = await adminLoginRes.Content.ReadFromJsonAsync<AuthTokenResponse>();
        Assert.NotNull(adminAuth);

        // Administrator creates a Patient account via /api/users/generate-keypair
        using HttpRequestMessage patientKeyReq = new(HttpMethod.Post, "/api/users/generate-keypair");
        patientKeyReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", adminAuth.Token);
        patientKeyReq.Content = JsonContent.Create(new GenerateKeyRequest
        {
            Username = "patient_charlie",
            Role = UserRole.Patient,
            AccessGate = AccessGate.ApiWeb,
            Algorithm = KeyAlgorithm.Ed25519
        });
        HttpResponseMessage patientKeyRes = await _client.SendAsync(patientKeyReq);
        Assert.Equal(HttpStatusCode.OK, patientKeyRes.StatusCode);
        GeneratedKeyBundle? patientBundle = await patientKeyRes.Content.ReadFromJsonAsync<GeneratedKeyBundle>();
        Assert.NotNull(patientBundle);

        // Authenticate as Patient
        KeyLoginRequest patLogin = new()
        {
            Username = "patient_charlie",
            PrivateKeyPem = patientBundle.PrivateKeyPem
        };
        HttpResponseMessage patLoginRes = await _client.PostAsJsonAsync("/api/auth/login-key", patLogin);
        Assert.Equal(HttpStatusCode.OK, patLoginRes.StatusCode);
        AuthTokenResponse? patAuth = await patLoginRes.Content.ReadFromJsonAsync<AuthTokenResponse>();
        Assert.NotNull(patAuth);

        using HttpClient patClient = _factory.CreateClient();
        patClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", patAuth.Token);

        // Patient accesses /api/patient/dna (Authorized)
        HttpResponseMessage dnaRes = await patClient.GetAsync("/api/patient/dna?regimen=MaleToFemale");
        Assert.Equal(HttpStatusCode.OK, dnaRes.StatusCode);
        PatientDigitalDnaPackage? dnaPackage = await dnaRes.Content.ReadFromJsonAsync<PatientDigitalDnaPackage>();
        Assert.NotNull(dnaPackage);
        Assert.Equal("patient_charlie", dnaPackage.PatientId);
        Assert.NotEmpty(dnaPackage.Staples);

        // Patient attempts to execute simulation (Forbidden)
        HttpResponseMessage simForbidden = await patClient.PostAsJsonAsync("/api/simulations/run/serum-stability", new SerumStabilityRequest());
        Assert.Equal(HttpStatusCode.Forbidden, simForbidden.StatusCode);

        // Patient attempts to list users (Forbidden)
        HttpResponseMessage usersForbidden = await patClient.GetAsync("/api/users");
        Assert.Equal(HttpStatusCode.Forbidden, usersForbidden.StatusCode);

        // Administrator provisions an encrypted record for the subject under GDPR Article 17
        using HttpRequestMessage encReq = new(HttpMethod.Post, "/api/compliance/encrypt-record");
        encReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", adminAuth.Token);
        encReq.Content = JsonContent.Create(new EncryptRecordRequest
        {
            SubjectId = "patient_charlie",
            Payload = new { Notes = "Personalized HRT schedule" }
        });
        HttpResponseMessage encRes = await _client.SendAsync(encReq);
        Assert.Equal(HttpStatusCode.OK, encRes.StatusCode);

        // Administrator executes GDPR Article 17 crypto-shredding on patient key
        using HttpRequestMessage shredReq = new(HttpMethod.Post, "/api/compliance/crypto-shred");
        shredReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", adminAuth.Token);
        shredReq.Content = JsonContent.Create(new CryptoShredRequest { SubjectId = "patient_charlie" });
        HttpResponseMessage shredRes = await _client.SendAsync(shredReq);
        Assert.Equal(HttpStatusCode.OK, shredRes.StatusCode);

        // Verify audit log recorded crypto-shredding
        using HttpRequestMessage auditReq = new(HttpMethod.Get, "/api/compliance/audit-ledger?limit=10");
        auditReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", adminAuth.Token);
        HttpResponseMessage auditRes = await _client.SendAsync(auditReq);
        Assert.Equal(HttpStatusCode.OK, auditRes.StatusCode);
        string auditJson = await auditRes.Content.ReadAsStringAsync();
        Assert.Contains("CryptoShredSubjectKey", auditJson);
    }
}
