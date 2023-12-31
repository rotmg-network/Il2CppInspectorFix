﻿/*
    Copyright 2017-2021 Katy Coe - http://www.djkaty.com - https://github.com/djkaty

    All rights reserved.
*/

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace Il2CppInspector.Reflection {
    public class PropertyInfo : MemberInfo
    {
        // IL2CPP-specific data
        public Il2CppPropertyDefinition Definition { get; }
        public int Index { get; }
        // Root definition: the property with Definition != null
        protected readonly PropertyInfo rootDefinition;

        // Information/flags about the property
        public PropertyAttributes Attributes { get; }

        public bool CanRead => GetMethod != null;
        public bool CanWrite => SetMethod != null;

        // Custom attributes for this member
        public override IEnumerable<CustomAttributeData> CustomAttributes => CustomAttributeData.GetCustomAttributes(rootDefinition);

        public MethodInfo GetMethod { get; }
        public MethodInfo SetMethod { get; }

        public bool IsAutoProperty => DeclaringType.DeclaredFields.Any(f => f.Name == $"<{Name}>k__BackingField");

        public override string CSharpName {
            get {
                // Explicit interface implementation
                if (DeclaringType.ImplementedInterfaces
                    .FirstOrDefault(i => Name.IndexOf("." + i.CSharpName, StringComparison.Ordinal) != -1) is TypeInfo @interface)
                    return Name.Substring(Name.IndexOf("." + @interface.CSharpName, StringComparison.Ordinal) + 1);

                // Regular property
                return Name;
            }
        }

        public TypeInfo PropertyType => GetMethod?.ReturnType ?? SetMethod.DeclaredParameters[^1].ParameterType;

        public override MemberTypes MemberType => MemberTypes.Property;

        public PropertyInfo(Il2CppInspector pkg, int propIndex, TypeInfo declaringType) :
            base(declaringType) {
            Index = propIndex;
            Definition = pkg.Properties[propIndex];
            MetadataToken = (int) Definition.token;
            Name = pkg.Strings[Definition.nameIndex];
            rootDefinition = this;

            // Copy attributes
            Attributes = (PropertyAttributes) Definition.attrs;

            // prop.get and prop.set are method indices from the first method of the declaring type
            if (Definition.get >= 0)
                GetMethod = declaringType.DeclaredMethods.First(x => x.Index == declaringType.Definition.methodStart + Definition.get);
            if (Definition.set >= 0)
                SetMethod = declaringType.DeclaredMethods.First(x => x.Index == declaringType.Definition.methodStart + Definition.set);
        }

        // Create a property based on a get and set method
        public PropertyInfo(MethodInfo getter, MethodInfo setter, TypeInfo declaringType) :
            base(declaringType) {
            Index = -1;
            Definition = null;
            rootDefinition = this;

            Name = (getter ?? setter).Name.Replace(".get_", ".").Replace(".set_", ".");
            GetMethod = getter;
            SetMethod = setter;
        }

        public PropertyInfo(PropertyInfo propertyDef, TypeInfo declaringType) : base(declaringType) {
            rootDefinition = propertyDef;

            Name = propertyDef.Name;
            if (propertyDef.GetMethod != null)
                GetMethod = declaringType.GetMethodByDefinition(propertyDef.GetMethod);
            if (propertyDef.SetMethod != null)
                SetMethod = declaringType.GetMethodByDefinition(propertyDef.SetMethod);
        }
    }
}