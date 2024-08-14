using System.Runtime.InteropServices;

namespace SlickDirectory;

public class GlobalHotKey : IEquatable<GlobalHotKey>
{
    private readonly int _modifiers;
    private readonly int _key;
    private readonly IntPtr _hWnd;
    private readonly int _id;

    public GlobalHotKey(ModKeys modifier, Keys key, IntPtr formHandle)
    {
        _modifiers = (int)modifier;
        _key = (int)key;
        _hWnd = formHandle;
        _id = this.CalculateGetHashCode();
    }

    private int CalculateGetHashCode()
    {
        unchecked
        {
            var hashCode = _modifiers;
            hashCode = (hashCode * 397) ^ _key;
            hashCode = (hashCode * 397) ^ _hWnd.GetHashCode();
            return hashCode;
        }
    }

    public bool Register()
    {
        return RegisterHotKey(_hWnd, _id, _modifiers, _key);
    }

    public bool Unregister()
    {
        return UnregisterHotKey(_hWnd, _id);
    }

    public bool Equals(GlobalHotKey? other)
    {
        if (ReferenceEquals(null, other)) return false;
        if (ReferenceEquals(this, other)) return true;
        return _modifiers == other._modifiers && _key == other._key && _hWnd.Equals(other._hWnd);
    }

    public override bool Equals(object? obj)
    {
        if (ReferenceEquals(null, obj)) return false;
        if (ReferenceEquals(this, obj)) return true;
        if (obj.GetType() != this.GetType()) return false;
        return Equals((GlobalHotKey)obj);
    }

    public override int GetHashCode()
    {
        return _id;
    }


    public static bool operator ==(GlobalHotKey? left, GlobalHotKey? right)
    {
        return Equals(left, right);
    }

    public static bool operator !=(GlobalHotKey? left, GlobalHotKey? right)
    {
        return !Equals(left, right);
    }


    [DllImport("user32.dll")]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, int fsModifiers, int vk);

    [DllImport("user32.dll")]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    public override string ToString()
    {
        return $"{nameof(_modifiers)}: {_modifiers}, {nameof(_key)}: {_key}";
    }
}