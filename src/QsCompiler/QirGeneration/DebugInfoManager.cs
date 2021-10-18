// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Reflection;
using Microsoft.Quantum.QIR;
using Microsoft.Quantum.QsCompiler.SyntaxTree;
using Ubiquity.NET.Llvm;
using Ubiquity.NET.Llvm.DebugInfo;

namespace Microsoft.Quantum.QsCompiler.QIR
{
    /// <summary>
    /// This class holds shared utility routines for generating QIR debug information.
    /// It contains a reference to the GenerationContext that owns it because it needs
    /// access to the same shared state represented by the GenerationContext.
    /// </summary>
    internal sealed class DebugInfoManager
    {
        #region Member variables

        /// <summary>
        /// Dwarf version we are using for the debug info in the QIR generation
        /// </summary>
        private static readonly uint DWARF_VERSION = 4;

        /// <summary>
        /// Title of the CodeView module flag for debug info
        /// </summary>
        private static readonly string CODEVIEW_NAME = "CodeView";

        /// <summary>
        /// CodeView version we are using for the debug info in the QIR generation
        /// </summary>
        private static readonly uint CODEVIEW_VERSION = 1;

        /// <summary>
        /// Whether or not to emit debug information during QIR generation
        /// </summary>
        public bool DebugFlag { get; internal set; } = true;

        /// <summary>
        /// Contains the location information for the syntax tree node we are currently parsing and its parents
        /// </summary>
        internal Stack<QsLocation> LocationStack { get; }

        /// <summary>
        /// The GenerationContext that owns this DebugInfoManager
        /// </summary>
        private readonly GenerationContext sharedState;

        #endregion

        #region Shortcut access to member variables' content

        /// <summary>
        /// Access to the GenerationContext's Module
        /// </summary>
        internal BitcodeModule Module => this.sharedState.Module;

        /// <summary>
        /// Access to the GenerationContext's Module's DICompileUnit
        /// </summary>
        internal DICompileUnit? DICompileUnit => this.sharedState.Module.DICompileUnit;

        /// <summary>
        /// Access to the GenerationContext's Module's DIBuilder
        /// </summary>
        internal DebugInfoBuilder DIBuilder => this.sharedState.Module.DIBuilder;

        /// <summary>
        /// Access to the GenerationContext's Context
        /// </summary>
        internal Context Context => this.sharedState.Context;

        #endregion

        internal DebugInfoManager(GenerationContext generationContext)
        {
            this.sharedState = generationContext;
            this.LocationStack = new Stack<QsLocation>();
        }

        #region Top Level Debug Functions

        /// <summary>
        /// Gets the Dwarf version we are using for the debug info in the QIR generation
        /// </summary>
        internal uint GetDwarfVersion() => DWARF_VERSION;

        /// <summary>
        /// Gets the CodeView version we are using for the debug info in the QIR generation
        /// </summary>
        internal string GetCodeViewName() => CODEVIEW_NAME;

        /// <summary>
        /// Gets the title for the CodeView module flag for debug info
        /// </summary>
        internal uint GetCodeViewVersion() => CODEVIEW_VERSION;

        /// <summary>
        /// If DebugFlag is set to false, simply creates a module for the owning GenerationContext, attaches it to its Context, and returns it.
        /// If DebugFlag is set to true, creates a module with a compile unit and top level debug info, attaches it, and returns it.
        /// Note: because this is called from within the constructor of the GenerationContext,
        /// we cannot access this.Module or anything else that uses this.sharedState
        /// </summary>
        internal BitcodeModule CreateModuleWithCompileUnit()
        {
            if (this.DebugFlag)
            {
                // Find an entry point in order to find the source file path
                bool foundEntryAttribute = false;
                string sourcePath = "";

                ImmutableDictionary<QsQualifiedName, QsCallable> globalCallables = this.sharedState.GetGlobalCallables();
                foreach (QsCallable callable in globalCallables.Values)
                {
                    if (foundEntryAttribute)
                    {
                        break;
                    }

                    foreach (QsDeclarationAttribute atr in callable.Attributes)
                    {
                        string atrName = atr.TypeId.IsValue ? atr.TypeId.Item.Name : "";
                        if (atrName == AttributeNames.EntryPoint)
                        {
                            sourcePath = callable.Source.CodeFile;
                            foundEntryAttribute = true;
                            break;
                        }
                    }
                }

                if (!foundEntryAttribute)
                {
                    throw new Exception("No entry point found in the source code");
                }

                string moduleID = Path.GetFileName(sourcePath);
                AssemblyName compilationAssemblyName = CompilationLoader.GetQSharpCompilerAssemblyName();
                string producerIdent = compilationAssemblyName.Name + " V." + compilationAssemblyName.Version;

                // TODO: If we need compilation flags in the future we will need to figure out how to get the compile options in the CompilationLoader.Configuration
                // and turn them into a string (although the compilation flags don't seem to be emitted to the IR anyways)
                string compilationFlags = "";

                // Change the extension for to .c because of the language/extension issue
                string cSourcePath = Path.ChangeExtension(sourcePath, ".c");

                BitcodeModule newModule = this.Context.CreateBitcodeModule(
                    moduleID,
                    SourceLanguage.C99, // For now, we are using the C interface. Ideally this would be a user defined language for Q#
                    cSourcePath, // Note that to debug the source file, you'll have to copy the content of the .qs file into a .c file with the same name
                    producerIdent,
                    compilationFlags: compilationFlags);

                // Add Module identity and Module Flags
                newModule.AddProducerIdentMetadata(producerIdent);

                // TODO: ModuleFlagBehavior.Warning is emitting a 1 (indicating error) instead of 2
                newModule.AddModuleFlag(ModuleFlagBehavior.Warning, BitcodeModule.DwarfVersionValue, this.GetDwarfVersion());
                newModule.AddModuleFlag(ModuleFlagBehavior.Warning, BitcodeModule.DebugVersionValue, BitcodeModule.DebugMetadataVersion);
                newModule.AddModuleFlag(ModuleFlagBehavior.Warning, this.GetCodeViewName(), this.GetCodeViewVersion()); // TODO: We seem to need this flag and not Dwarf in order to debug on Windows. Need to look into why LLDB is using CodeView on Windows
                AddTargetSpecificModuleFlags();

                return newModule;
            }
            else
            {
                return this.Context.CreateBitcodeModule();
            }

            void AddTargetSpecificModuleFlags()
            {
                // TODO: could be useful to have target-specific module flags at some point
                // Examples: AddModuleFlag(ModuleFlagBehavior.Error, "PIC Level", 2); (ModuleFlagBehavior.Error, "wchar_size", 4); (ModuleFlagBehavior.Error, "min_enum_size", 4)
                // Have access to newModule here
                return;
            }
        }

        #endregion

    }
}
