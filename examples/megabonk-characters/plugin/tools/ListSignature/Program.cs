using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;

// Read-only metadata dump. Opens a DLL as bytes and prints decoded signatures.
// It does not execute the assembly and does not print method bodies.
// An exact type name is not capped. A substring match stays short and says so.
internal static class Program
{
    const int BroadTypeCap = 40;
    const int BroadMemberCap = 40;

    static int Main(string[] args)
    {
        if (args.Length < 2)
        {
            Console.Error.WriteLine("usage: ListSignature <dll> <ExactType> [MemberOrExactType...]");
            Console.Error.WriteLine("Exact type names print every field, method, and constructor with decoded signatures.");
            Console.Error.WriteLine("Extra tokens that are not type names keep only those members, plus constructors.");
            return 2;
        }
        var path = args[0];
        var tokens = new List<string>();
        for (int i = 1; i < args.Length; i++) tokens.Add(args[i]);
        using var stream = File.OpenRead(path);
        using var pe = new PEReader(stream);
        var reader = pe.GetMetadataReader();
        var types = new List<TypeDefinitionHandle>();
        foreach (var handle in reader.TypeDefinitions) types.Add(handle);

        var typeTokens = new List<string>();
        var memberTokens = new List<string>();
        for (int i = 0; i < tokens.Count; i++)
        {
            if (AnyExact(reader, types, tokens[i])) typeTokens.Add(tokens[i]);
            else memberTokens.Add(tokens[i]);
        }

        if (typeTokens.Count > 0)
        {
            int shown = 0;
            foreach (var handle in types)
            {
                var type = reader.GetTypeDefinition(handle);
                if (!Exact(reader, type, typeTokens)) continue;
                Dump(reader, type, memberTokens, 0);
                shown++;
            }
            if (shown == 0) Console.WriteLine("no types matched");
            return 0;
        }

        int broad = 0;
        foreach (var handle in types)
        {
            var type = reader.GetTypeDefinition(handle);
            var name = reader.GetString(type.Name);
            var full = FullName(reader, type.Namespace, type.Name);
            if (!Contains(full, name, tokens)) continue;
            broad++;
            if (broad > BroadTypeCap)
            {
                Console.WriteLine("... truncated at " + BroadTypeCap + " types; pass the exact type name for every member and decoded signatures");
                break;
            }
            Dump(reader, type, memberTokens, BroadMemberCap);
        }
        if (broad == 0) Console.WriteLine("no types matched");
        else Console.WriteLine("substring match is capped; pass the exact type name for constructors and full signatures");
        return 0;
    }

    static void Dump(MetadataReader reader, TypeDefinition type, List<string> members, int cap)
    {
        var full = FullName(reader, type.Namespace, type.Name);
        var baseName = BaseName(reader, type.BaseType);
        Console.WriteLine("TYPE " + full + (baseName.Length == 0 ? "" : " : " + baseName));
        int count = 0;
        foreach (var fieldHandle in type.GetFields())
        {
            var field = reader.GetFieldDefinition(fieldHandle);
            var name = reader.GetString(field.Name);
            if (!Want(name, members, false)) continue;
            if (cap > 0 && ++count > cap)
            {
                Console.WriteLine("  ... fields truncated; pass the exact type name");
                break;
            }
            Console.WriteLine("  FIELD " + Access(field.Attributes) + " " + name + " : " + FieldType(reader, field.Signature));
        }
        count = 0;
        foreach (var methodHandle in type.GetMethods())
        {
            var method = reader.GetMethodDefinition(methodHandle);
            var name = reader.GetString(method.Name);
            bool ctor = name == ".ctor" || name == ".cctor";
            if (!Want(name, members, ctor)) continue;
            if (cap > 0 && ++count > cap)
            {
                Console.WriteLine("  ... methods truncated; pass the exact type name");
                break;
            }
            var kind = ctor ? "CTOR" : "METHOD";
            string args;
            string ret;
            if (!TryMethod(reader, method, out args, out ret))
                Console.WriteLine("  " + kind + " " + MethodFlags(method.Attributes) + " " + name + " params=" + ParamCount(reader, method.Signature) + " signature UNKNOWN");
            else
                Console.WriteLine("  " + kind + " " + MethodFlags(method.Attributes) + " " + name + " (" + args + ") -> " + ret);
        }
    }

    static bool Want(string name, List<string> members, bool ctor)
    {
        if (ctor) return true;
        if (members == null || members.Count == 0) return true;
        for (int i = 0; i < members.Count; i++)
        {
            var token = members[i];
            if (token == "ctor") token = ".ctor";
            if (token == "cctor") token = ".cctor";
            if (string.Equals(name, token, StringComparison.OrdinalIgnoreCase)) return true;
        }
        return false;
    }

    static bool AnyExact(MetadataReader reader, List<TypeDefinitionHandle> types, string token)
    {
        foreach (var handle in types)
        {
            if (Exact(reader, reader.GetTypeDefinition(handle), new List<string> { token })) return true;
        }
        return false;
    }

    static bool Exact(MetadataReader reader, TypeDefinition type, List<string> tokens)
    {
        var name = reader.GetString(type.Name);
        var full = FullName(reader, type.Namespace, type.Name);
        for (int i = 0; i < tokens.Count; i++)
        {
            var token = tokens[i];
            if (string.Equals(name, token, StringComparison.Ordinal)) return true;
            if (string.Equals(full, token, StringComparison.Ordinal)) return true;
        }
        return false;
    }

    static bool Contains(string full, string name, List<string> tokens)
    {
        for (int i = 0; i < tokens.Count; i++)
        {
            var token = tokens[i];
            if (full.IndexOf(token, StringComparison.OrdinalIgnoreCase) >= 0) return true;
            if (name.IndexOf(token, StringComparison.OrdinalIgnoreCase) >= 0) return true;
        }
        return false;
    }

    static string FullName(MetadataReader reader, StringHandle nsHandle, StringHandle nameHandle)
    {
        var ns = reader.GetString(nsHandle);
        var name = reader.GetString(nameHandle);
        return ns.Length == 0 ? name : ns + "." + name;
    }

    static string BaseName(MetadataReader reader, EntityHandle handle)
    {
        if (handle.Kind == HandleKind.TypeDefinition)
        {
            var type = reader.GetTypeDefinition((TypeDefinitionHandle)handle);
            return FullName(reader, type.Namespace, type.Name);
        }
        if (handle.Kind == HandleKind.TypeReference)
        {
            var type = reader.GetTypeReference((TypeReferenceHandle)handle);
            return FullName(reader, type.Namespace, type.Name);
        }
        return "";
    }

    static int ParamCount(MetadataReader reader, BlobHandle signature)
    {
        var blob = reader.GetBlobReader(signature);
        if (blob.RemainingBytes < 2) return -1;
        blob.ReadByte();
        return blob.ReadCompressedInteger();
    }

    static string FieldType(MetadataReader reader, BlobHandle signature)
    {
        try
        {
            var provider = new TypeNames(reader);
            var decoder = new SignatureDecoder<string, object>(provider, reader, null);
            var blob = reader.GetBlobReader(signature);
            return decoder.DecodeFieldSignature(ref blob);
        }
        catch (Exception)
        {
            return "UNKNOWN";
        }
    }

    static bool TryMethod(MetadataReader reader, MethodDefinition method, out string args, out string ret)
    {
        args = "";
        ret = "";
        try
        {
            var provider = new TypeNames(reader);
            var decoder = new SignatureDecoder<string, object>(provider, reader, null);
            var blob = reader.GetBlobReader(method.Signature);
            var sig = decoder.DecodeMethodSignature(ref blob);
            ret = sig.ReturnType ?? "UNKNOWN";
            var parts = new List<string>();
            int index = 0;
            foreach (var parameter in sig.ParameterTypes)
            {
                index++;
                var pname = ParameterName(reader, method, index);
                parts.Add(pname.Length == 0 ? parameter : parameter + " " + pname);
            }
            args = string.Join(", ", parts);
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    static string ParameterName(MetadataReader reader, MethodDefinition method, int sequence)
    {
        foreach (var handle in method.GetParameters())
        {
            var parameter = reader.GetParameter(handle);
            if (parameter.SequenceNumber == sequence) return reader.GetString(parameter.Name);
        }
        return "";
    }

    static string Access(FieldAttributes attributes)
    {
        switch (attributes & FieldAttributes.FieldAccessMask)
        {
            case FieldAttributes.Public: return "public";
            case FieldAttributes.Private: return "private";
            case FieldAttributes.Family: return "protected";
            case FieldAttributes.Assembly: return "internal";
            case FieldAttributes.FamORAssem: return "protected internal";
            default: return "private";
        }
    }

    static string MethodFlags(MethodAttributes attributes)
    {
        string access;
        switch (attributes & MethodAttributes.MemberAccessMask)
        {
            case MethodAttributes.Public: access = "public"; break;
            case MethodAttributes.Private: access = "private"; break;
            case MethodAttributes.Family: access = "protected"; break;
            case MethodAttributes.Assembly: access = "internal"; break;
            case MethodAttributes.FamORAssem: access = "protected internal"; break;
            default: access = "private"; break;
        }
        if ((attributes & MethodAttributes.Static) != 0) access += " static";
        else access += " instance";
        return access;
    }

    sealed class TypeNames : ISignatureTypeProvider<string, object>
    {
        public TypeNames(MetadataReader reader) { }

        public string GetPrimitiveType(PrimitiveTypeCode typeCode)
        {
            return typeCode.ToString();
        }

        public string GetTypeFromDefinition(MetadataReader reader, TypeDefinitionHandle handle, byte rawTypeKind)
        {
            var type = reader.GetTypeDefinition(handle);
            return FullName(reader, type.Namespace, type.Name);
        }

        public string GetTypeFromReference(MetadataReader reader, TypeReferenceHandle handle, byte rawTypeKind)
        {
            var type = reader.GetTypeReference(handle);
            return FullName(reader, type.Namespace, type.Name);
        }

        public string GetSZArrayType(string elementType) { return elementType + "[]"; }
        public string GetPointerType(string elementType) { return elementType + "*"; }
        public string GetByReferenceType(string elementType) { return "ref " + elementType; }
        public string GetPinnedType(string elementType) { return elementType; }

        public string GetGenericInstantiation(string genericType, ImmutableArray<string> typeArguments)
        {
            return genericType + "<" + string.Join(", ", typeArguments) + ">";
        }

        public string GetArrayType(string elementType, ArrayShape shape)
        {
            return elementType + "[,]";
        }

        public string GetGenericMethodParameter(object genericContext, int index) { return "!!" + index; }
        public string GetGenericTypeParameter(object genericContext, int index) { return "!" + index; }

        public string GetModifiedType(string modifier, string unmodifiedType, bool isRequired)
        {
            return unmodifiedType;
        }

        public string GetFunctionPointerType(MethodSignature<string> signature)
        {
            return "method";
        }

        public string GetTypeFromSpecification(MetadataReader reader, object genericContext, TypeSpecificationHandle handle, byte rawTypeKind)
        {
            var spec = reader.GetTypeSpecification(handle);
            var blob = reader.GetBlobReader(spec.Signature);
            return new SignatureDecoder<string, object>(this, reader, genericContext).DecodeType(ref blob, true);
        }
    }
}

// Built into this assembly so the tool can be checked without a game DLL.
internal sealed class SignatureFixture
{
    public SignatureFixture(int count, string name) { }
    public static void Sample(ref int value, string[] names) { }
}
