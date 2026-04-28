using Assetra.Core.Models.MonteCarlo;

namespace Assetra.Core.Interfaces.MonteCarlo;

public interface IMonteCarloSimulator
{
    /// <summary>
    /// 跑 N 條退休現金流路徑並彙整成功率與百分位。
    /// </summary>
    MonteCarloResult Simulate(MonteCarloInputs inputs);
}
