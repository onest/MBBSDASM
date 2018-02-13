﻿using System;
using System.IO;
using System.Linq;
using System.Text;
using MBBSDASM.Artifacts;
using MBBSDASM.Enums;

namespace MBBSDASM
{
    /// <summary>
    ///     Main Console Entrypoint
    /// </summary>
    class Program
    {
        static void Main(string[] args)
        {
            Console.WriteLine("------------------------------------------------------");
            Console.WriteLine("MBBSDASM v1.3");
            Console.WriteLine("GitHub: http://www.github.com/enusbaum/mbbsdasm/");
            Console.WriteLine("------------------------------------------------------");

            if (args.Length == 0)
            {
                Console.WriteLine("Please use the -? option for help");
                return;
            }
            
            try
            {
                //Command Line Values
                var sInputFile = "";
                var sOutputFile = "";
                var bMinimal = false;
                var bAnalysis = false;
                var bStrings = false;
                for (var i = 0; i < args.Length; i++)
                {
                    switch (args[i].ToUpper())
                    {
                        case "-I":
                            sInputFile = args[i + 1];
                            i++;
                            break;
                        case "-O":
                            sOutputFile = args[i + 1];
                            i++;
                            break;
                        case "-MINIMAL":
                            bMinimal = true;
                            break;
                        case "-ANALYSIS":
                            bAnalysis = true;
                            break;
                        case "-STRINGS":
                            bStrings = true;
                            break;
                        case "-?":
                            Console.WriteLine("-I <file> -- Input File to Disassemble");
                            Console.WriteLine("-O <file> -- Output File for Disassembly (Default Console)");
                            Console.WriteLine("-MINIMAL -- Minimal Disassembler Output");
                            Console.WriteLine("-ANALYSIS -- Additional Analysis on Imported Functions (if available)");
                            Console.WriteLine("-STRINGS -- Output all strings found in DATA segments at end of Disassembly");
                            return;
                    }
                }

                //Verify Input File is Valid
                if (string.IsNullOrEmpty(sInputFile) || !File.Exists(sInputFile))
                    throw new Exception("Error: Please specify a valid input file");

                //Warn of Analysis not being available with minimal output
                if (bMinimal && bAnalysis)
                    Console.WriteLine($"{DateTime.Now} Warning: Analysis Mode unavailable with minimal output option, ignoring");

                Console.WriteLine($"{DateTime.Now} Inspecting File: {sInputFile}");

                //Read the entire file to memory
                var inputFile = new NEFile(sInputFile);
                
                //Decompile Each Segment
                foreach (var s in inputFile.SegmentTable)
                {
                    Console.WriteLine($"{DateTime.Now} Performing Disassembly of Segment {s.Ordinal}");
                    s.DisassemblyLines = Dasm.Disassembler.Disassemble(s);
                }

                //Skip Additional Analysis if they selected minimal
                if (!bMinimal)
                {
                    Console.WriteLine($"{DateTime.Now} Extracting Strings from DATA Segments");
                    Dasm.Disassembler.ProcessStrings(inputFile);
                    
                    Console.WriteLine($"{DateTime.Now} Applying Relocation Info ");
                    Dasm.Disassembler.ApplyRelocationInfo(inputFile);

                    Console.WriteLine($"{DateTime.Now} Applying String References");
                    Dasm.Disassembler.ResolveStringReferences(inputFile);

                    Console.WriteLine($"{DateTime.Now} Resolving Jump Targets");
                    Dasm.Disassembler.ResolveJumpTargets(inputFile);

                    Console.WriteLine($"{DateTime.Now} Resolving Call Targets");
                    Dasm.Disassembler.ResolveCallTargets(inputFile);

                    Console.WriteLine($"{DateTime.Now} Identifying Entry Points");
                    Dasm.Disassembler.IdentifyEntryPoints(inputFile);

                    //Apply Selected Analysis
                    if (bAnalysis)
                    {
                        Console.WriteLine($"{DateTime.Now} Performing Additional Analysis");
                        Analysis.Analyzer.Analyze(inputFile);
                    }
                }

                Console.WriteLine($"{DateTime.Now} Writing Disassembly Output");
                
                //Build Final Output
                var output = new StringBuilder();
                
                output.AppendLine($"; Disassembly of {inputFile.Path}{inputFile.FileName}");
                output.AppendLine($"; Description: {inputFile.NonResidentNameTable[0].Name}");
                output.AppendLine(";");
                output.AppendLine(";-------------------------------------------");
                output.AppendLine("; Segment Information");
                output.AppendLine($"; Number of Code/Data Segments = {inputFile.WindowsHeader.SegmentTableEntries}");
                output.AppendLine(";-------------------------------------------");
                foreach (var s in inputFile.SegmentTable)
                {
                    output.AppendLine(
                        $"; Segment #{s.Ordinal:0000}\tOffset: {s.Offset:X8}\tSize: {s.Data.Length:X4}\t Flags: 0x{s.Flag:X4} -> {(s.Flags.Contains(EnumSegmentFlags.Code) ? "CODE" : "DATA")}, {(s.Flags.Contains(EnumSegmentFlags.Fixed) ? "FIXED" : "MOVABLE")}");
                }
                
                output.AppendLine(";-------------------------------------------");
                output.AppendLine("; Entry Table Information");
                output.AppendLine($"; Number of Entry Table Functions = {inputFile.EntryTable.Count}");
                output.AppendLine(";-------------------------------------------");
                foreach (var t in inputFile.NonResidentNameTable)
                {
                    if (t.IndexIntoEntryTable == 0)
                        continue;
                    
                    output.AppendLine($"; Addr:{inputFile.EntryTable.FirstOrDefault(x=> x.Ordinal == t.IndexIntoEntryTable)?.SegmentNumber:0000}.{inputFile.EntryTable.FirstOrDefault(x=> x.Ordinal == t.IndexIntoEntryTable)?.Offset:X4}\tOrd:{t.IndexIntoEntryTable:0000}d\tName: {t.Name}");
                }
                foreach (var t in inputFile.ResidentNameTable)
                {
                    if (t.IndexIntoEntryTable == 0)
                        continue;
                    
                    output.AppendLine($"; Addr:{inputFile.EntryTable.FirstOrDefault(x=> x.Ordinal == t.IndexIntoEntryTable)?.SegmentNumber:0000}.{inputFile.EntryTable.FirstOrDefault(x=> x.Ordinal == t.IndexIntoEntryTable)?.Offset:X4}\tOrd:{t.IndexIntoEntryTable:0000}d\tName: {t.Name}");
                }
                
                output.AppendLine(";");

                //Write Disassembly to output
                foreach (var s in inputFile.SegmentTable.Where(x => x.Flags.Contains(EnumSegmentFlags.Code)))
                {
                    output.AppendLine(";-------------------------------------------");
                    output.AppendLine($"; Start of Code for Segment {s.Ordinal}");
                    output.AppendLine("; FILE_OFFSET:SEG_NUM.SEG_OFFSET");
                    output.AppendLine(";-------------------------------------------");

                    //Allows us to line up all the comments in a segment along the same column
                    var maxDecodeLength = s.DisassemblyLines.Max(x => x.Disassembly.ToString().Length) + 21;
                    
                    //Write each line of the disassembly to the output stream
                    foreach (var d in s.DisassemblyLines)
                    {
                        //Label Entrypoints/Exported Functions
                        if (d.ExportedFunction != null)
                        {
                            d.Comments.Add($"Exported Function: {d.ExportedFunction.Name}");
                        }

                        //Label Branch Targets
                        foreach (var b in d.BranchFromRecords)
                        {
                            switch (b.BranchType)
                            {
                                case EnumBranchType.Call:
                                    d.Comments.Add(
                                        $"Referenced by CALL at address: {b.Segment:0000}.{b.Offset:X4}h {(b.IsRelocation ? "(Relocation)" : string.Empty)}");
                                    break;
                                case EnumBranchType.Conditional:
                                case EnumBranchType.Unconditional:
                                    d.Comments.Add($"{(b.BranchType == EnumBranchType.Conditional ? "Conditional" : "Unconditional")} jump from {b.Segment:0000}:{b.Offset:X4}h");
                                    break;
                            }
                        }
                        
                        //Label Branch Origins (Relocation)
                        foreach (var b in d.BranchToRecords.Where(x => x.IsRelocation && x.BranchType == EnumBranchType.Call))
                            d.Comments.Add($"CALL {b.Segment:0000}.{b.Offset:X4}h (Relocation)");
                        
                        //Label Refereces by SEG ADDR (Internal)
                        foreach(var b in d.BranchToRecords.Where(x=> x.IsRelocation && x.BranchType == EnumBranchType.SegAddr))
                            d.Comments.Add($"SEG ADDR of Segment {b.Segment}");

                        //Label String References
                        if(d.StringReference != null)
                            d.Comments.Add($"Possible String reference from SEG {d.StringReference.Segment} -> \"{d.StringReference.Value}\"");
                        
                        //Only label Imports if Analysis is off, because Analysis does much more in-depth labeling
                        if (!bAnalysis)
                        {
                            foreach(var b in d.BranchToRecords.Where(x=> x.IsRelocation && (x.BranchType == EnumBranchType.CallImport || x.BranchType == EnumBranchType.SegAddrImport)))
                                d.Comments.Add($"{(b.BranchType == EnumBranchType.CallImport ? "call" : "SEG ADDR of" )} {inputFile.ImportedNameTable.First(x => x.Ordinal == b.Segment).Name}.Ord({b.Offset:X4}h)");
                        }
                        
                        var sOutputLine = $"{d.Disassembly.Offset + s.Offset:X8}h:{s.Ordinal:0000}.{d.Disassembly.Offset:X4}h {d.Disassembly}";
                        if (d.Comments != null && d.Comments.Count > 0)
                        {
                            var newLine = false;
                            var firstCommentIndex = 0;
                            foreach (var c in d.Comments)
                            {
                                if (!newLine)
                                {  
                                    sOutputLine += $"{new string(' ', maxDecodeLength - sOutputLine.Length)}; {c}";
                                    
                                    //Set variables to help us keep the following comments lined up with the first one
                                    firstCommentIndex = sOutputLine.IndexOf(';');
                                    newLine = true;
                                    continue;
                                }
                                sOutputLine += $"\r\n{new string(' ', firstCommentIndex) }; {c}";                                
                            }
                        }
                        output.AppendLine(sOutputLine);
                    }
                    output.AppendLine();
                }

                //Write Strings to Output
                if (bStrings)
                {
                    Console.WriteLine($"{DateTime.Now} Writing Strings Output");
                    
                    foreach (var seg in inputFile.SegmentTable.Where(x =>
                        x.Flags.Contains(EnumSegmentFlags.Data) && x.StringRecords.Count > 0))
                    {
                        output.AppendLine(";-------------------------------------------");
                        output.AppendLine($"; Start of Data for Segment {seg.Ordinal}");
                        output.AppendLine("; FILE_OFFSET:SEG_NUM.SEG_OFFSET");
                        output.AppendLine(";-------------------------------------------");
                        foreach (var str in seg.StringRecords)
                            output.AppendLine(
                                $"{str.Offset + str.Offset:X8}h:{seg.Ordinal:0000}.{str.Offset:X4}h '{str.Value}'");
                    }
                }

                if (string.IsNullOrEmpty(sOutputFile))
                {
                    Console.WriteLine(output.ToString());
                }
                else
                {
                    Console.WriteLine($"{DateTime.Now} Writing Disassembly to {sOutputFile}");
                    File.WriteAllText(sOutputFile, output.ToString());
                }
                Console.WriteLine($"{DateTime.Now} Done!");
            }
            catch (Exception e)
            {
                Console.WriteLine(e);
                Console.WriteLine($"{DateTime.Now} Fatal Exception -- Exiting");
            }
        }
    }
}