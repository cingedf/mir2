using System.Drawing;

public class InIReader
{
    #region Fields
    private readonly List<string> _contents;
    private readonly string _fileName;
    #endregion

    #region Constructor
    public InIReader(string fileName)
    {
        _fileName = fileName;

        if (!Directory.Exists(Path.GetDirectoryName(fileName)))
        {
            Directory.CreateDirectory(Path.GetDirectoryName(fileName));
        }

        _contents = new List<string>();
        try
        {
            if (File.Exists(_fileName))
                _contents.AddRange(File.ReadAllLines(_fileName));
        }
        catch
        {
        }
    }
    #endregion

    #region Functions
    private string FindValue(string section, string key)
    {
        for (int a = 0; a < _contents.Count; a++)
            if (String.CompareOrdinal(_contents[a], "[" + section + "]") == 0)
                for (int b = a + 1; b < _contents.Count; b++)
                {
                    // 20261116 审计修复：无 '=' 的行 Split 后只有 [0]，直接取 [1] 会越界
                    // 20261117 审计修复P2：Split('=') 按所有 '=' 拆分只取 [1]，值含 '='（密码/URL）被截断；限制为 2 段保留完整值
                    string[] parts = _contents[b].Split(new[] { '=' }, 2);
                    if (String.CompareOrdinal(parts[0], key) == 0)
                        return parts.Length > 1 ? parts[1] : string.Empty;
                    else if (_contents[b].StartsWith("[") && _contents[b].EndsWith("]"))
                        return null;
                }
        return null;
    }

    private int FindIndex(string section, string key)
    {
        for (int a = 0; a < _contents.Count; a++)
            if (String.CompareOrdinal(_contents[a], "[" + section + "]") == 0)
                for (int b = a + 1; b < _contents.Count; b++)
                    // 20261117 审计修复P2：Split('=', 2) 只按首个 '=' 拆，key 判定不受值中 '=' 影响
                    if (String.CompareOrdinal(_contents[b].Split(new[] { '=' }, 2)[0], key) == 0)
                        return b;
                    else if (_contents[b].StartsWith("[") && _contents[b].EndsWith("]"))
                    {
                        _contents.Insert(b - 1, key + "=");
                        return b - 1;
                    }
                    else if (_contents.Count - 1 == b)
                    {
                        _contents.Add(key + "=");
                        return _contents.Count - 1;
                    }
        if (_contents.Count > 0)
            _contents.Add("");

        _contents.Add("[" + section + "]");
        _contents.Add(key + "=");
        return _contents.Count - 1;
    }

    public void Save()
    {
        try
        {
            // 20261116 审计修复：先写临时文件再原子替换，避免写一半崩溃/并发读写留下损坏 ini
            string tmp = _fileName + ".tmp";
            File.WriteAllLines(tmp, _contents);
            if (File.Exists(_fileName))
                File.Replace(tmp, _fileName, null);
            else
                File.Move(tmp, _fileName);
        }
        catch
        {
        }
    }
    #endregion

    #region Read
    public bool ReadBoolean(string section, string key, bool Default, bool writeWhenNull = true)
    {
        bool result;

        if (!bool.TryParse(FindValue(section, key), out result))
        {
            result = Default;
            if (writeWhenNull) Write(section, key, Default);
        }

        return result;
    }

    public byte ReadByte(string section, string key, byte Default, bool writeWhenNull = true)
    {
        byte result;

        if (!byte.TryParse(FindValue(section, key), out result))
        {
            result = Default;
            if (writeWhenNull) Write(section, key, Default);
        }


        return result;
    }

    public sbyte ReadSByte(string section, string key, sbyte Default, bool writeWhenNull = true)
    {
        sbyte result;

        if (!sbyte.TryParse(FindValue(section, key), out result))
        {
            result = Default;
            if (writeWhenNull) Write(section, key, Default);
        }


        return result;
    }

    public ushort ReadUInt16(string section, string key, ushort Default, bool writeWhenNull = true)
    {
        ushort result;

        if (!ushort.TryParse(FindValue(section, key), out result))
        {
            result = Default;
            if (writeWhenNull) Write(section, key, Default);
        }


        return result;
    }

    public short ReadInt16(string section, string key, short Default, bool writeWhenNull = true)
    {
        short result;

        if (!short.TryParse(FindValue(section, key), out result))
        {
            result = Default;
            if (writeWhenNull) Write(section, key, Default);
        }


        return result;
    }

    public uint ReadUInt32(string section, string key, uint Default, bool writeWhenNull = true)
    {
        uint result;

        if (!uint.TryParse(FindValue(section, key), out result))
        {
            result = Default;
            if (writeWhenNull) Write(section, key, Default);
        }

        return result;
    }

    public int ReadInt32(string section, string key, int Default, bool writeWhenNull = true)
    {
        int result;

        if (!int.TryParse(FindValue(section, key), out result))
        {
            result = Default;
            if (writeWhenNull) Write(section, key, Default);
        }

        return result;
    }

    public ulong ReadUInt64(string section, string key, ulong Default, bool writeWhenNull = true)
    {
        ulong result;

        if (!ulong.TryParse(FindValue(section, key), out result))
        {
            result = Default;
            if (writeWhenNull) Write(section, key, Default);
        }

        return result;
    }

    public long ReadInt64(string section, string key, long Default, bool writeWhenNull = true)
    {
        long result;

        if (!long.TryParse(FindValue(section, key), out result))
        {
            result = Default;
            if (writeWhenNull) Write(section, key, Default);
        }


        return result;
    }

    public float ReadSingle(string section, string key, float Default, bool writeWhenNull = true)
    {
        float result;

        if (!float.TryParse(FindValue(section, key), out result))
        {
            result = Default;
            if (writeWhenNull) Write(section, key, Default);
        }

        return result;
    }

    public double ReadDouble(string section, string key, double Default, bool writeWhenNull = true)
    {
        double result;

        if (!double.TryParse(FindValue(section, key), out result))
        {
            result = Default;
            if (writeWhenNull) Write(section, key, Default);
        }

        return result;
    }

    public decimal ReadDecimal(string section, string key, decimal Default, bool writeWhenNull = true)
    {
        decimal result;

        if (!decimal.TryParse(FindValue(section, key), out result))
        {
            result = Default;
            if (writeWhenNull) Write(section, key, Default);
        }

        return result;
    }

    public string ReadString(string section, string key, string Default, bool writeWhenNull = true)
    {
        string result = FindValue(section, key);

        if (string.IsNullOrEmpty(result))
        {
            result = Default;

            if (writeWhenNull) Write(section, key, Default);
        }

        return result;
    }

    public char ReadChar(string section, string key, char Default, bool writeWhenNull = true)
    {
        char result;

        if (!char.TryParse(FindValue(section, key), out result))
        {
            result = Default;
            if (writeWhenNull) Write(section, key, Default);
        }

        return result;
    }

    public Point ReadPoint(string section, string key, Point Default)
    {
        // 20261117 审计修复P2：畸形配置（缺逗号/空段/非数字）原 Split(',')[1] 越界抛异常导致启动中断；统一钳制回写默认值
        string temp = FindValue(section, key);
        int tempX, tempY;
        string[] parts = temp == null ? null : temp.Split(',');
        if (parts == null || parts.Length < 2 || !int.TryParse(parts[0], out tempX) || !int.TryParse(parts[1], out tempY))
        {
            Write(section, key, Default);
            return Default;
        }

        return new Point(tempX, tempY);
    }

    public Size ReadSize(string section, string key, Size Default)
    {
        // 20261117 审计修复P2：同 ReadPoint，防 Split(',')[1] 越界
        string temp = FindValue(section, key);
        int tempX, tempY;
        string[] parts = temp == null ? null : temp.Split(',');
        if (parts == null || parts.Length < 2 || !int.TryParse(parts[0], out tempX) || !int.TryParse(parts[1], out tempY))
        {
            Write(section, key, Default);
            return Default;
        }

        return new Size(tempX, tempY);
    }

    public TimeSpan ReadTimeSpan(string section, string key, TimeSpan Default)
    {
        TimeSpan result;

        if (!TimeSpan.TryParse(FindValue(section, key), out result))
        {
            result = Default;
            Write(section, key, Default);
        }


        return result;
    }

    public float ReadFloat(string section, string key, float Default)
    {
        float result;

        if (!float.TryParse(FindValue(section, key), out result))
        {
            result = Default;
            Write(section, key, Default);
        }

        return result;
    }
    #endregion

    #region Write
    public void Write(string section, string key, bool value)
    {
        _contents[FindIndex(section, key)] = key + "=" + value;
        Save();
    }

    public void Write(string section, string key, byte value)
    {
        _contents[FindIndex(section, key)] = key + "=" + value;
        Save();
    }

    public void Write(string section, string key, sbyte value)
    {
        _contents[FindIndex(section, key)] = key + "=" + value;
        Save();
    }

    public void Write(string section, string key, ushort value)
    {
        _contents[FindIndex(section, key)] = key + "=" + value;
        Save();
    }

    public void Write(string section, string key, short value)
    {
        _contents[FindIndex(section, key)] = key + "=" + value;
        Save();
    }

    public void Write(string section, string key, uint value)
    {
        _contents[FindIndex(section, key)] = key + "=" + value;
        Save();
    }

    public void Write(string section, string key, int value)
    {
        _contents[FindIndex(section, key)] = key + "=" + value;
        Save();
    }

    public void Write(string section, string key, ulong value)
    {
        _contents[FindIndex(section, key)] = key + "=" + value;
        Save();
    }

    public void Write(string section, string key, long value)
    {
        _contents[FindIndex(section, key)] = key + "=" + value;
        Save();
    }

    public void Write(string section, string key, float value)
    {
        _contents[FindIndex(section, key)] = key + "=" + value;
        Save();
    }

    public void Write(string section, string key, double value)
    {
        _contents[FindIndex(section, key)] = key + "=" + value;
        Save();
    }

    public void Write(string section, string key, decimal value)
    {
        _contents[FindIndex(section, key)] = key + "=" + value;
        Save();
    }

    public void Write(string section, string key, string value)
    {
        _contents[FindIndex(section, key)] = key + "=" + value;
        Save();
    }

    public void Write(string section, string key, char value)
    {
        _contents[FindIndex(section, key)] = key + "=" + value;
        Save();
    }

    public void Write(string section, string key, Point value)
    {
        _contents[FindIndex(section, key)] = key + "=" + value.X + "," + value.Y;
        Save();
    }

    public void Write(string section, string key, Size value)
    {
        _contents[FindIndex(section, key)] = key + "=" + value.Width + "," + value.Height;
        Save();
    }

    public void Write(string section, string key, TimeSpan value)
    {
        _contents[FindIndex(section, key)] = key + "=" + value;
        Save();
    }
    #endregion
}