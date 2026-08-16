using System.Diagnostics.CodeAnalysis;

namespace WaveLinkBackup.Core.Results;

/// <summary>
/// Success or an expected failure. Hand-rolled rather than taken from a package: it is
/// forty lines, and Core carries no third-party dependencies.
/// </summary>
public readonly struct Result<T>
{
    private readonly T? value;

    private Result(T? value, CoreError? error)
    {
        this.value = value;
        Error = error;
    }

    public CoreError? Error { get; }

    [MemberNotNullWhen(false, nameof(Error))]
    public bool IsSuccess => Error is null;

    /// <summary>The value. Throws if this is a failure - reading it without checking is a bug.</summary>
    public T Value => IsSuccess
        ? value!
        : throw new InvalidOperationException(
            $"Result was a failure ({Error.GetType().Name}); check IsSuccess first.");

    public static Result<T> Ok(T value) => new(value, null);
    public static Result<T> Fail(CoreError error) => new(default, error);

    public static implicit operator Result<T>(T value) => Ok(value);
    public static implicit operator Result<T>(CoreError error) => Fail(error);

    /// <summary>Carries a failure across a type change without restating it.</summary>
    public Result<TOther> Propagate<TOther>() => IsSuccess
        ? throw new InvalidOperationException("Cannot propagate a success as a failure.")
        : Result<TOther>.Fail(Error);
}

/// <summary>A result with nothing to return - the operation either worked or it did not.</summary>
public readonly struct Result
{
    private Result(CoreError? error) => Error = error;

    public CoreError? Error { get; }

    [MemberNotNullWhen(false, nameof(Error))]
    public bool IsSuccess => Error is null;

    public static Result Ok() => new(null);
    public static Result Fail(CoreError error) => new(error);

    public static implicit operator Result(CoreError error) => Fail(error);
}
