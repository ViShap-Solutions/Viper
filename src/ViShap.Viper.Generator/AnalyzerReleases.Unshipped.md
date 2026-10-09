; Unshipped analyzer release
; https://github.com/dotnet/roslyn-analyzers/blob/main/src/Microsoft.CodeAnalysis.Analyzers/ReleaseTrackingAnalyzers.Help.md

### New Rules

Rule ID | Category | Severity | Notes
--------|----------|----------|-------
VPR001 | ViShap.Viper | Error | A [BinaryContext] class must be partial
VPR002 | ViShap.Viper | Error | A [BinaryContext] class must be a non-generic, non-abstract class deriving from BinarySerializerContext
VPR003 | ViShap.Viper | Error | A [BinaryContext] class must not declare what the generator writes
VPR004 | ViShap.Viper | Error | [BinaryKey] on a type that is not [BinaryContract]
VPR005 | ViShap.Viper | Error | [BinaryInclude] together with [BinaryIgnore]
VPR006 | ViShap.Viper | Error | Duplicate [BinaryOrder] value
VPR007 | ViShap.Viper | Error | [BinaryInclude] on a member of a contract type
VPR008 | ViShap.Viper | Error | [BinaryOrder] on a member of a contract type
VPR009 | ViShap.Viper | Error | [BinaryKey] together with [BinaryIgnore]
VPR010 | ViShap.Viper | Error | Contract member without [BinaryKey] or [BinaryIgnore]
VPR011 | ViShap.Viper | Error | Negative [BinaryKey] value
VPR012 | ViShap.Viper | Error | Duplicate [BinaryKey] value
VPR013 | ViShap.Viper | Error | Delegate member
VPR014 | ViShap.Viper | Error | Member type with no representation on the wire
VPR015 | ViShap.Viper | Error | [BinaryUnion] tag outside 0-255
VPR016 | ViShap.Viper | Error | Duplicate [BinaryUnion] tag
VPR017 | ViShap.Viper | Error | [BinaryUnion] type not assignable to the base
VPR018 | ViShap.Viper | Warning | Abstract type or interface without [BinaryUnion]
VPR019 | ViShap.Viper | Warning | Type described by reflection
