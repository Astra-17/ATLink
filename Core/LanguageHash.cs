using System.Text;
namespace ATLink.Core;
public static class LanguageHash
{
    public static uint Compute(string key)
    {
        uint value=0;
        foreach(byte b in Encoding.UTF8.GetBytes(key))
        {
            uint crc=(value^(uint)(b&0xdf))&255;
            for(int i=0;i<8;i++)crc=(crc&1)!=0?(crc>>1)^0xedb88320u:crc>>1;
            value=(value>>8)^crc;
        }
        return value^0x80000000u;
    }
}
