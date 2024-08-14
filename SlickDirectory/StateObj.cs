namespace SlickDirectory;

public class StateObj : IEquatable<StateObj>
{
    public string TempDirectory { get; set; }

    #region Equality

    public bool Equals(StateObj? other)
    {
        if (ReferenceEquals(null, other)) return false;
        if (ReferenceEquals(this, other)) return true;
        return string.Equals(TempDirectory, other.TempDirectory, StringComparison.Ordinal);
    }

    public override bool Equals(object? obj)
    {
        if (ReferenceEquals(null, obj)) return false;
        if (ReferenceEquals(this, obj)) return true;
        if (obj.GetType() != this.GetType()) return false;
        return Equals((StateObj)obj);
    }

    public override int GetHashCode()
    {
        return StringComparer.OrdinalIgnoreCase.GetHashCode(TempDirectory);
    }

    public static bool operator ==(StateObj? left, StateObj? right)
    {
        return Equals(left, right);
    }

    public static bool operator !=(StateObj? left, StateObj? right)
    {
        return !Equals(left, right);
    }

    #endregion
}