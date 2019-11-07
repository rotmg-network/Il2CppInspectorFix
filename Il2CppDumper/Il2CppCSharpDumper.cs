﻿// Copyright (c) 2017-2019 Katy Coe - https://www.djkaty.com - https://github.com/djkaty
// All rights reserved

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using Il2CppInspector.Reflection;
using MethodInfo = Il2CppInspector.Reflection.MethodInfo;
using TypeInfo = Il2CppInspector.Reflection.TypeInfo;

namespace Il2CppInspector
{
    public class Il2CppCSharpDumper
    {
        private readonly Il2CppModel model;

        // Namespace prefixes whose contents should be skipped
        public List<string> ExcludedNamespaces { get; set; }

        // Suppress types, fields and methods with the CompilerGenerated attribute; suppress the attribute itself from property getters and setters
        public bool SuppressGenerated { get; set; }

        private const string CGAttribute = "System.Runtime.CompilerServices.CompilerGeneratedAttribute";
        private const string FBAttribute = "System.Runtime.CompilerServices.FixedBufferAttribute";

        public Il2CppCSharpDumper(Il2CppModel model) => this.model = model;

        private StreamWriter writer;

        public void WriteFile(string outFile) {
            using (writer = new StreamWriter(new FileStream(outFile, FileMode.Create), Encoding.UTF8)) {
                foreach (var asm in model.Assemblies) {
                    writer.Write($"// Image {asm.Index}: {asm.FullName} - {asm.Definition.typeStart}\n");

                    // Assembly-level attributes
                    writer.Write(asm.CustomAttributes.ToString(attributePrefix: "assembly: "));
                    if (asm.CustomAttributes.Any())
                        writer.Write("\n");
                }
                writer.Write("\n");

                foreach (var type in model.Assemblies.SelectMany(x => x.DefinedTypes)) {

                    // Skip namespace and any children if requested
                    if (ExcludedNamespaces?.Any(x => x == type.Namespace || type.Namespace.StartsWith(x + ".")) ?? false)
                        continue;

                    // Assembly.DefinedTypes returns nested types in the assembly by design - ignore them
                    if (!type.IsNested) {
                        writeType(type);
                        writer.Write("\n");
                    }
                }
            }
        }

        private void writeType(TypeInfo type, string prefix = "") {
            // Don't output compiler-generated types if desired
            if (SuppressGenerated && type.GetCustomAttributes(CGAttribute).Any())
                return;

            // Only print namespace if we're not nested
            if (!type.IsNested)
                writer.Write($"{prefix}// Namespace: {(!string.IsNullOrEmpty(type.Namespace)? type.Namespace : "<default namespace>")}\n");

            // Type declaration
            if (type.IsImport)
                writer.Write(prefix + "[ComImport]\n");
            if (type.IsSerializable)
                writer.Write(prefix + "[Serializable]\n");

            // Custom attributes
            writer.Write(type.CustomAttributes.ToString(prefix));

            writer.Write(prefix);
            if (type.IsPublic || type.IsNestedPublic)
                writer.Write("public ");
            if (type.IsNestedPrivate)
                writer.Write("private ");
            if (type.IsNestedFamily)
                writer.Write("protected ");
            if (type.IsNestedAssembly || type.IsNotPublic)
                writer.Write("internal ");
            if (type.IsNestedFamORAssem)
                writer.Write("protected internal ");
            if (type.IsNestedFamANDAssem)
                writer.Write("private protected ");

            // Roll-up multicast delegates to use the 'delegate' syntactic sugar
            if (type.IsClass && type.IsSealed && type.BaseType?.FullName == "System.MulticastDelegate") {
                var del = type.DeclaredMethods.First(x => x.Name == "Invoke");
                // IL2CPP doesn't seem to retain return type attributes
                //writer.Write(del.ReturnType.CustomAttributes.ToString(prefix, "return: "));
                writer.Write($"delegate {del.ReturnType.CSharpName} {type.CSharpTypeDeclarationName}(");

                bool first = true;
                foreach (var param in del.DeclaredParameters) {
                    if (!first)
                        writer.Write(", ");
                    first = false;
                    if (param.IsOptional)
                        writer.Write("optional ");
                    if (param.IsOut)
                        writer.Write("out ");
                    writer.Write($"{param.ParameterType.CSharpName} {param.Name}");
                }
                writer.Write($"); // TypeDefIndex: {type.Index}; {del.VirtualAddress.ToAddressString()}\n");
                return;
            }

            // An abstract sealed class is a static class
            if (type.IsAbstract && type.IsSealed)
                writer.Write("static ");
            else {
                if (type.IsAbstract && !type.IsInterface)
                    writer.Write("abstract ");
                if (type.IsSealed && !type.IsValueType && !type.IsEnum)
                    writer.Write("sealed ");
            }
            if (type.IsInterface)
                writer.Write("interface ");
            else if (type.IsValueType)
                writer.Write("struct ");
            else if (type.IsEnum)
                writer.Write("enum ");
            else
                writer.Write("class ");

            var @base = type.ImplementedInterfaces.Select(x => x.CSharpName).ToList();
            if (type.BaseType != null && type.BaseType.FullName != "System.Object" && type.BaseType.FullName != "System.ValueType" && !type.IsEnum)
                @base.Insert(0, type.BaseType.CSharpName);
            if (type.IsEnum && type.GetEnumUnderlyingType().FullName != "System.Int32") // enums derive from int by default
                @base.Insert(0, type.GetEnumUnderlyingType().CSharpName);
            var baseText = @base.Count > 0 ? " : " + string.Join(", ", @base) : string.Empty;

            writer.Write($"{type.CSharpTypeDeclarationName}{baseText} // TypeDefIndex: {type.Index}\n" + prefix + "{\n");

            // Fields
            if (!type.IsEnum) {
                if (type.DeclaredFields.Any())
                    writer.Write(prefix + "\t// Fields\n");

                foreach (var field in type.DeclaredFields) {
                    if (SuppressGenerated && field.GetCustomAttributes(CGAttribute).Any())
                        continue;

                    if (field.IsNotSerialized)
                        writer.Write(prefix + "\t[NonSerialized]\n");

                    // Attributes
                    writer.Write(field.CustomAttributes.Where(a => a.AttributeType.FullName != FBAttribute).ToString(prefix + "\t"));

                    writer.Write(prefix + "\t");
                    if (field.IsPrivate)
                        writer.Write("private ");
                    if (field.IsPublic)
                        writer.Write("public ");
                    if (field.IsFamily)
                        writer.Write("protected ");
                    if (field.IsAssembly)
                        writer.Write("internal ");
                    if (field.IsFamilyOrAssembly)
                        writer.Write("protected internal ");
                    if (field.IsFamilyAndAssembly)
                        writer.Write("private protected ");
                    if (field.IsLiteral)
                        writer.Write("const ");
                    // All const fields are also static by implication
                    else if (field.IsStatic)
                        writer.Write("static ");
                    if (field.IsInitOnly)
                        writer.Write("readonly ");
                    if (field.IsPinvokeImpl)
                        writer.Write("extern ");
                    if (field.GetCustomAttributes(FBAttribute).Any())
                        writer.Write($"fixed /* {((ulong) field.CustomAttributes.First(a => a.AttributeType.FullName == FBAttribute).VirtualAddress).ToAddressString()} */" +
                                     $" {field.FieldType.DeclaredFields.First(f => f.Name == "FixedElementField").FieldType.CSharpName} {field.Name}[0]");
                    else
                        writer.Write($"{field.FieldType.CSharpName} {field.Name}");
                    if (field.HasDefaultValue)
                        writer.Write($" = {field.DefaultValueString}");
                    writer.Write(";");
                    // Don't output field indices for const fields (they don't have any storage)
                    if (!field.IsLiteral)
                        writer.Write(" // 0x{0:X2}", (uint)field.Offset);
                    writer.Write("\n");
                }
                if (type.DeclaredFields.Any())
                    writer.Write("\n");
            }

            // Enumeration
            else {
                writer.Write(string.Join(",\n", type.GetEnumNames().Zip(type.GetEnumValues().OfType<object>(),
                    (k, v) => new { k, v }).OrderBy(x => x.v).Select(x => $"{prefix}\t{x.k} = {x.v}")) + "\n");
            }

            var usedMethods = new List<MethodInfo>();

            // Properties
            if (type.DeclaredProperties.Any())
                writer.Write(prefix + "\t// Properties\n");

            foreach (var prop in type.DeclaredProperties) {
                // Attributes
                writer.Write(prop.CustomAttributes.ToString(prefix + "\t"));

                // The access mask enum values go from 1 (private) to 6 (public) in order from most to least restrictive
                var getAccess = (prop.GetMethod?.Attributes ?? 0) & MethodAttributes.MemberAccessMask;
                var setAccess = (prop.SetMethod?.Attributes ?? 0) & MethodAttributes.MemberAccessMask;

                string modifiers = getAccess > setAccess? prop.GetMethod.GetModifierString() : prop.SetMethod.GetModifierString();
                writer.Write($"{prefix}\t{modifiers}{prop.PropertyType.CSharpName} {prop.Name} {{ ");
                writer.Write((prop.CanRead? prop.GetMethod.CustomAttributes.Where(a => !SuppressGenerated || a.AttributeType.FullName != CGAttribute).ToString(inline: true) 
                                               + (getAccess < setAccess? prop.GetMethod.GetAccessModifierString() : "") + "get; " : "")
                             + (prop.CanWrite? prop.SetMethod.CustomAttributes.Where(a => !SuppressGenerated || a.AttributeType.FullName != CGAttribute).ToString(inline: true) 
                                               + (setAccess < getAccess? prop.SetMethod.GetAccessModifierString() : "") + "set; " : "") + "}");
                if ((prop.CanRead && prop.GetMethod.VirtualAddress != 0) || (prop.CanWrite && prop.SetMethod.VirtualAddress != 0))
                    writer.Write(" // ");
                writer.Write((prop.CanRead && prop.GetMethod.VirtualAddress != 0 ? prop.GetMethod.VirtualAddress.ToAddressString() + " " : "")
                            + (prop.CanWrite && prop.SetMethod.VirtualAddress != 0 ? prop.SetMethod.VirtualAddress.ToAddressString() : "") + "\n");
                usedMethods.Add(prop.GetMethod);
                usedMethods.Add(prop.SetMethod);
            }
            if (type.DeclaredProperties.Any())
                writer.Write("\n");

            // Events
            if (type.DeclaredEvents.Any())
                writer.Write(prefix + "\t// Events\n");

            foreach (var evt in type.DeclaredEvents) {
                // Attributes
                writer.Write(evt.CustomAttributes.ToString(prefix + "\t"));

                string modifiers = evt.AddMethod?.GetModifierString();
                writer.Write($"{prefix}\t{modifiers}event {evt.EventHandlerType.CSharpName} {evt.Name} {{\n");
                var m = new Dictionary<string, ulong>();
                if (evt.AddMethod != null) m.Add("add", evt.AddMethod.VirtualAddress);
                if (evt.RemoveMethod != null) m.Add("remove", evt.RemoveMethod.VirtualAddress);
                if (evt.RaiseMethod != null) m.Add("raise", evt.RaiseMethod.VirtualAddress);
                writer.Write(string.Join("\n", m.Select(x => $"{prefix}\t\t{x.Key}; // {x.Value.ToAddressString()}")) + "\n" + prefix + "\t}\n");
                usedMethods.Add(evt.AddMethod);
                usedMethods.Add(evt.RemoveMethod);
                usedMethods.Add(evt.RaiseMethod);
            }
            if (type.DeclaredEvents.Any())
                writer.Write("\n");

            // Nested types
            if (type.DeclaredNestedTypes.Any())
                writer.Write(prefix + "\t// Nested types\n");

            foreach (var nestedType in type.DeclaredNestedTypes) {
                writeType(nestedType, prefix + "\t");
                writer.Write("\n");
            }

            // Constructors
            if (type.DeclaredConstructors.Any())
                writer.Write(prefix + "\t// Constructors\n");

            foreach (var method in type.DeclaredConstructors) {
                // Attributes
                writer.Write(method.CustomAttributes.ToString(prefix + "\t"));

                writer.Write($"{prefix}\t{method.GetModifierString()}{method.DeclaringType.UnmangledBaseName}{method.GetTypeParametersString()}(");
                writer.Write(method.GetParametersString());
                writer.Write(");" + (method.VirtualAddress != 0 ? $" // {method.VirtualAddress.ToAddressString()}" : "") + "\n");
            }
            if (type.DeclaredConstructors.Any())
                writer.Write("\n");

            // Methods
            if (type.DeclaredMethods.Except(usedMethods).Any())
                writer.Write(prefix + "\t// Methods\n");

            // Don't re-output methods for constructors, properties, events etc.
            foreach (var method in type.DeclaredMethods.Except(usedMethods)) {
                if (SuppressGenerated && method.GetCustomAttributes(CGAttribute).Any())
                    continue;

                // Attributes
                writer.Write(method.CustomAttributes.ToString(prefix + "\t"));
                // IL2CPP doesn't seem to retain return type attributes
                //writer.Write(method.ReturnType.CustomAttributes.ToString(prefix + "\t", "return: "));
                writer.Write($"{prefix}\t{method.GetModifierString()}");
                if (method.Name != "op_Implicit" && method.Name != "op_Explicit")
                    writer.Write($"{method.ReturnParameter.GetReturnParameterString()} {method.CSharpName}{method.GetTypeParametersString()}");
                else
                    writer.Write($"{method.CSharpName}{method.ReturnType.CSharpName}");
                writer.Write("(" + method.GetParametersString());
                writer.Write(");" + (method.VirtualAddress != 0 ? $" // {method.VirtualAddress.ToAddressString()}" : "") + "\n");
            }
            writer.Write(prefix + "}\n");
        }
    }
}