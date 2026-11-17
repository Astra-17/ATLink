using System.Buffers.Binary;
using ZstdSharp;
namespace ATLink.Core;

// Career saves also contain state outside the football DB. Only extract the DB;
// exporting a raw copy must never pretend to rebuild a playable career save.
internal static class CareerFile
{
    public static bool IsContainer(ReadOnlySpan<byte> bytes)
    {
        if(!SquadFile.IsContainer(bytes))return false;
        long prefix=18L+BinaryPrimitives.ReadInt32LittleEndian(bytes[10..14]);
        return prefix>=18&&prefix+"SaveType_Career"u8.Length<=bytes.Length&&bytes.Slice((int)prefix,"SaveType_Career"u8.Length).SequenceEqual("SaveType_Career"u8);
    }
    public static DatabaseDocument Open(byte[] bytes,string metadata,string path)
    {
        // BNRY byte buffers store UInt64 decoded/encoded lengths before a Zstd frame.
        // Search structurally rather than using save-specific offsets.
        var frames=new List<byte[]>();
        ReadOnlySpan<byte> zstdSignature=[0x28,0xB5,0x2F,0xFD];
        long totalDecoded=0;
        for(int offset=16;offset<=bytes.Length-4;offset++){
            if(!bytes.AsSpan(offset,4).SequenceEqual(zstdSignature))continue;
            ulong decoded=BinaryPrimitives.ReadUInt64LittleEndian(bytes.AsSpan(offset-16,8)),encoded=BinaryPrimitives.ReadUInt64LittleEndian(bytes.AsSpan(offset-8,8));
            if(decoded==0||decoded>128*1024*1024||totalDecoded+(long)decoded>128*1024*1024||encoded==0||encoded>(ulong)(bytes.Length-offset))continue;
            using var decoder=new Decompressor();
            var frame=decoder.Unwrap(bytes.AsSpan(offset,(int)encoded),maxDecompressedSize:128*1024*1024).ToArray();
            if((ulong)frame.Length!=decoded)throw new InvalidDataException(UiText.Get("Invalid career database compression block."));
            totalDecoded+=frame.Length;frames.Add(frame);offset+=(int)encoded-1;
        }
        var candidates=new List<DatabaseDocument>();
        foreach(var payload in frames.Count>0?frames:new List<byte[]>{bytes}){
        var data=payload;
        ReadOnlySpan<byte> signature=[68,66,0,8,0,0,0,0];
        for(int offset=0;offset<=data.Length-28;offset++){
            if(!data.AsSpan(offset,8).SequenceEqual(signature))continue;
            int size=BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(offset+8,4)),count=BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(offset+16,4));
            if(size<28||(long)offset+size>data.Length||count<1||count>(size-28)/8)continue;
            try{
                var db=DatabaseDocument.FromBytes(data.AsSpan(offset,size).ToArray(),metadata,path);
                if(db.Tables.Any(t=>t.Name=="players")&&db.Tables.Any(t=>t.Name=="teams")&&db.Tables.Any(t=>t.Name=="teamplayerlinks"))candidates.Add(db);
            }catch(InvalidDataException){}catch(OverflowException){}
        }
        }
        if(candidates.Count!=1)throw new InvalidDataException(UiText.Get("The career file must contain one readable football database compatible with the selected game."));
        var result=candidates[0];result.IsCareer=true;return result;
    }
}
