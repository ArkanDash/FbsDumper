# FbsDumper
A tool to recover flatbuffers definitions from game assemblies.
It's exactly the same as [my old FBS dumper](https://github.com/Hiro420/FbsDumper), except it relies on asm instructions parsing.\
Please note that in x64 asm the process is much more complicated, so if the dumper fails on some classes it will proceed to use the FbsDumper-V1 method

- TODO: default values

## Originally made for Blue Archive, should theoretically work with other games but is untested.

# Usage 
- Specify the target variables in MainApp (currently hardcoded as I'm too lazy to de-hardcode)
- Build via Visual Studio 2022 or the command
```bash
dotnet build
```
- Run the tool (FbsDumper.exe)

# I DO NOT CLAIM ANY RESPONSIBILITY FOR ANY USAGE OF THIS SOFTWARE, THE SOFTWARE IS MADE 100% FOR EDUCATIONAL PURPOSES ONLY

Copyright© Hiro420