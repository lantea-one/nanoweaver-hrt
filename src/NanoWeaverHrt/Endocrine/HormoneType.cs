namespace NanoWeaverHrt.Endocrine;

/// <summary>
/// Identifies primary steroid hormones regulated by the synthetic endocrine architecture.
/// </summary>
/// <remarks>
/// <para>
/// <strong>Plain-English Summary:</strong> Steroid hormones are biochemical signaling molecules that 
/// instruct cells how to develop, metabolize, and maintain sexual characteristics.
/// </para>
/// </remarks>
public enum HormoneType
{
    /// <summary>
    /// 17β-Estradiol (E2): The primary human estrogen responsible for feminizing secondary sexual characteristics.
    /// Typical target serum range: 300 to 800 pmol/L (80 to 220 pg/mL).
    /// </summary>
    EstradiolE2,

    /// <summary>
    /// Testosterone (T): The primary androgenic steroid responsible for masculinizing secondary sexual characteristics.
    /// Typical target serum range: 10 to 30 nmol/L (300 to 1000 ng/dL).
    /// </summary>
    Testosterone,

    /// <summary>
    /// Dihydrotestosterone (DHT): Potent 5α-reduced androgen metabolite responsible for terminal hair patterning.
    /// </summary>
    Dihydrotestosterone,

    /// <summary>
    /// Progesterone (P4): Steroid hormone involved in reproductive tract homeostasis and neuroprotection.
    /// </summary>
    Progesterone
}

/// <summary>
/// Operational state of the synthetic endocrine toggle switch.
/// </summary>
/// <remarks>
/// <para>
/// <strong>Plain-English Summary:</strong> A bistable switch is a biological circuit that can rest in one of two stable 
/// operating modes (like a light switch). In our system, the switch can be locked into either an Estrogen-dominant 
/// or Testosterone-dominant regulatory state.
/// </para>
/// </remarks>
public enum ToggleState
{
    /// <summary>
    /// Estrogen-dominant mode: Aromatase (CYP19A1) and Estrogen Receptor (ERα/ERβ) pathways active;
    /// testosterone sequestration or synthesis suppression active.
    /// </summary>
    EstrogenicDominant,

    /// <summary>
    /// Testosterone-dominant mode: Androgen Receptor (AR) and 17β-HSD pathways active;
    /// estradiol synthesis suppressed.
    /// </summary>
    AndrogenicDominant,

    /// <summary>
    /// Transitional state during active switching or initial calibration.
    /// </summary>
    Transitioning
}

/// <summary>
/// Clinical and synthetic biology regimen objective for permanent hormone replacement therapy.
/// </summary>
/// <remarks>
/// <para>
/// <strong>Plain-English Summary:</strong> Specifies whether the delivery vehicle is programmed to 
/// establish permanent female-typical endocrine signaling (Male-to-Female / MtF) or male-typical 
/// endocrine signaling (Female-to-Male / FtM).
/// </para>
/// </remarks>
public enum TherapeuticRegimen
{
    /// <summary>
    /// Male-to-Female (MtF) HRT: Prioritizes 17β-estradiol synthesis and receptor activation 
    /// while continuously suppressing endogenous androgen levels to cisgender female physiological targets (&lt; 1.8 nmol/L).
    /// </summary>
    MaleToFemale,

    /// <summary>
    /// Female-to-Male (FtM) HRT: Prioritizes testosterone signaling and androgen receptor activation 
    /// while suppressing cyclic estrogen synthesis to cisgender male physiological targets (12 to 28 nmol/L).
    /// </summary>
    FemaleToMale,

    /// <summary>
    /// Customized non-binary or micro-dosed endocrine setpoint tailored to specific patient requirements.
    /// </summary>
    Custom
}

/// <summary>
/// Anatomical and cellular delivery target for the DNA origami nanocarrier vehicle.
/// </summary>
/// <remarks>
/// <para>
/// <strong>Plain-English Summary:</strong> To avoid off-target side effects, the DNA origami nanocarrier displays 
/// surface aptamers that home in on specific cell types in the body, releasing or sequestering hormones only where intended.
/// </para>
/// </remarks>
public enum TargetTissueType
{
    /// <summary>
    /// Anterior pituitary gonadotrope cells (GnRH receptors) for central feedback modulation of LH and FSH.
    /// </summary>
    AnteriorPituitaryGonadotrope,

    /// <summary>
    /// Adrenal cortex steroidogenic cells (modulating CYP enzymes for steroidogenesis).
    /// </summary>
    AdrenalSteroidogenic,

    /// <summary>
    /// Gonadal steroid-producing cells (Leydig or Theca/Granulosa cell pathways).
    /// </summary>
    GonadalTarget,

    /// <summary>
    /// Peripheral somatic tissues (breast, hair follicles, bone, adipose tissue) expressing ER or AR receptors.
    /// </summary>
    PeripheralReceptor
}
