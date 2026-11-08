using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;

// Read-only metadata dump. Opens a DLL as bytes and prints type and member names.
// It does not execute the assembly and does not print method bodies.
internal static class Program
{
    static int Main(string[] args)
    {
        if (args.Length < 2)
        {
            Console.Error.WriteLine("usage: ListSignature <dll> <name> [name...]");
            return 2;
        }
        var path = args[0];
        var filters = new List<string>();
        for (int i = 1; i < args.Length; i++) filters.Add(args[i]);
        using var stream = File.OpenRead(path);
        using var pe = new PEReader(stream);
        var reader = pe.GetMetadataReader();
        int shown = 0;
        foreach (var handle in reader.TypeDefinitions)
        {
            var type = reader.GetTypeDefinition(handle);
            var name = reader.GetString(type.Name);
            var ns = reader.GetString(type.Namespace);
            var full = ns.Length == 0 ? name : ns + "." + name;
            if (!Matches(full, name, filters)) continue;
            shown++;
            if (shown > 40)
            {
                Console.WriteLine("... truncated at 40 types");
                break;
            }
            var baseName = BaseName(reader, type.BaseType);
            Console.WriteLine("TYPE " + full + (baseName.Length == 0 ? "" : " : " + baseName));
            int members = 0;
            foreach (var fieldHandle in type.GetFields())
            {
                if (++members > 40) { Console.WriteLine("  ... fields truncated"); break; }
                var field = reader.GetFieldDefinition(fieldHandle);
                Console.WriteLine("  FIELD " + reader.GetString(field.Name));
            }
            members = 0;
            foreach (var methodHandle in type.GetMethods())
            {
                if (++members > 40) { Console.WriteLine("  ... methods truncated"); break; }
                var method = reader.GetMethodDefinition(methodHandle);
                Console.WriteLine("  METHOD " + reader.GetString(method.Name) + " params=" + ParamCount(reader, method.Signature));
            }
        }
        if (shown == 0) Console.WriteLine("no types matched");
        return 0;
    }

    static bool Matches(string full, string name, List<string> filters)
    {
        for (int i = 0; i < filters.Count; i++)
        {
            var filter = filters[i];
            if (full.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0) return true;
            if (name.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0) return true;
        }
        return false;
    }

    static string BaseName(MetadataReader reader, EntityHandle handle)
    {
        if (handle.Kind == HandleKind.TypeDefinition)
        {
            var type = reader.GetTypeDefinition((TypeDefinitionHandle)handle);
            return reader.GetString(type.Name);
        }
        if (handle.Kind == HandleKind.TypeReference)
        {
            var type = reader.GetTypeReference((TypeReferenceHandle)handle);
            var ns = reader.GetString(type.Namespace);
            var name = reader.GetString(type.Name);
            return ns.Length == 0 ? name : ns + "." + name;
        }
        return "";
    }

    static int ParamCount(MetadataReader reader, BlobHandle signature)
    {
        var readerBlob = reader.GetBlobReader(signature);
        if (readerBlob.RemainingBytes < 2) return -1;
        readerBlob.ReadByte();
        return readerBlob.ReadCompressedInteger();
    }
}
