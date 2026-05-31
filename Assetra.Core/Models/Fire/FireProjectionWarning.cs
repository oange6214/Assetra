namespace Assetra.Core.Models.Fire;

public enum FireProjectionWarningCode
{
    UnableToReachFireWithinProjection = 0,
    DrawdownDepletesBeforeLifeExpectancy = 1,
    WithdrawalRateAboveCommonRange = 2,
    AnnualSavingsBelowZero = 3,
    InflationMissingForNominalMode = 4,
    CurrentAgeMustBeLessThanLifeExpectancy = 5,
}

public sealed record FireProjectionWarning(
    FireProjectionWarningCode Code,
    string Message);
