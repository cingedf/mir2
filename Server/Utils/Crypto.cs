using System.Security.Cryptography;
using System.Text;

namespace Server.Utils
{
    class Crypto
    {
        public const int SaltSize = 24;
        public const int HashSize = 24;
        // 20261119 安全加固：PBKDF2 迭代从 50 提升到 100000（安全成本约提升 2000 倍，登录仅几十毫秒）。
        // 注意：迭代数是哈希输入的一部分，提升后所有以旧迭代数存储的密码哈希校验失败，需重置账号密码；
        // 本服仅一个自建测试账号，已确认可接受。
        public const int Iterations = 100000;
        public static byte[] GenerateSalt()
        {
            var rng = RandomNumberGenerator.Create();
            byte[] salt = new byte[SaltSize];
            rng.GetBytes(salt);

            return salt;
        }

        public static string HashPassword(string password, byte[] salt)
        {
            Rfc2898DeriveBytes pbkdf2 = new Rfc2898DeriveBytes(password, salt, Iterations, HashAlgorithmName.SHA1);
            return Encoding.UTF8.GetString(pbkdf2.GetBytes(HashSize));
        }
    }
}
