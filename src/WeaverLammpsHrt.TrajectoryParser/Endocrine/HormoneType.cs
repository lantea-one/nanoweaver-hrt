namespace WeaverLammpsHrt.TrajectoryParser.Endocrine;

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
