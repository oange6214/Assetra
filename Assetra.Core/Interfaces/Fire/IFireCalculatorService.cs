using Assetra.Core.Models.Fire;

namespace Assetra.Core.Interfaces.Fire;

public interface IFireCalculatorService
{
    /// <summary>
    /// 以年度為單位推算到達 FIRE 目標所需的年數與淨資產增長軌跡。
    /// </summary>
    FireProjection Calculate(FireInputs inputs);
}
