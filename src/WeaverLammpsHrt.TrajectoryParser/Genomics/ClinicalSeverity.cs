namespace WeaverLammpsHrt.TrajectoryParser.Genomics;

/// <summary>
/// Clinical severity and urgency classification for pharmacogenomic call-out alerts.
/// </summary>
/// <remarks>
/// <para>
/// <strong>Plain-English Summary:</strong> When analyzing patient genetics, variants range from 
/// completely benign (Optimal) to life-threatening contraindications (Critical, such as high blood clot risk).
/// This enum standardizes how urgently clinicians and bio-hackers must respond to a finding.
/// </para>
/// </remarks>
public enum ClinicalSeverity
{
    /// <summary>
    /// Wild-type or favorable allele with baseline physiological kinetics and optimal tolerance.
    /// </summary>
    Optimal,

    /// <summary>
    /// Minor kinetic variation or metabolic deviation. Monitoring recommended, but standard dosing acceptable.
    /// </summary>
    Advisory,

    /// <summary>
    /// Clinically significant alteration in hormone clearance, receptor transactivation, or delivery stability.
    /// Explicit adjustment to target setpoints or aptamer binding affinity is recommended.
    /// </summary>
    Warning,

    /// <summary>
    /// Severe contraindication or high-risk predisposition (e.g. Factor V Leiden venous thromboembolism).
    /// Immediate setpoint capping, elimination of peak burst kinetics, or mandatory chemical modification is required.
    /// </summary>
    Critical
}

/// <summary>
/// Biological domain category of the detected genetic variant or predisposition.
/// </summary>
/// <remarks>
/// <para>
/// <strong>Plain-English Summary:</strong> Categorizes genetic mutations by the specific physiological system they affect:
/// blood clotting (ThromboticRisk), hormone breakdown (SteroidMetabolism), target tissue response (ReceptorSensitivity),
/// or immune recognition of the DNA origami nanocarrier (NanocarrierImmunogenicity).
/// </para>
/// </remarks>
public enum GeneticCategory
{
    /// <summary>
    /// Coagulation and hemostasis pathways (e.g. F5, F2) directly relevant to estrogen-induced hypercoagulability.
    /// </summary>
    ThromboticRisk,

    /// <summary>
    /// Phase I and Phase II hepatic steroid metabolic enzymes (e.g. CYP1B1, CYP3A4, COMT, UGT2B17).
    /// </summary>
    SteroidMetabolism,

    /// <summary>
    /// Nuclear steroid hormone receptor density, binding affinity, and co-activator dynamics (e.g. AR CAG repeats, ESR1).
    /// </summary>
    ReceptorSensitivity,

    /// <summary>
    /// Innate pattern recognition receptors (e.g. TLR9) recognizing unmethylated CpG motifs on synthetic DNA origami nanocarriers.
    /// </summary>
    NanocarrierImmunogenicity
}
