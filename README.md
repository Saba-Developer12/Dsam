# Dsam - A .NET 9 Disassembler & Decompiler

![Dsam Screenshot 1](screenshots/1.png)

Dsam is a high-performance, cross-platform PE (Portable Executable) disassembler and decompiler written in C# and .NET 9. Designed for low-level system analysis, code flow visualization, and binary patching with custom control flow analysis.

## Features

*   **PE File Analysis:** In-depth analysis of Portable Executable files (`.exe`, `.dll`, `.sys`).
*   **High-Performance Disassembly:** Fast and accurate disassembly powered by Iced.
*   **C# Pseudocode Generation:** Generates readable C# pseudocode from disassembled x86/x64 instructions.
*   **Interactive UI:** A user-friendly interface built with WinForms for easy navigation and analysis.
    *   Hex View
    *   Disassembly View
    *   Pseudocode View
    *   Registers View
    *   Sections View
    *   Cross-references (Xrefs)
*   **Extensible Analysis:** The core library (`Dsam.Core`) can be used independently for custom analysis tasks.
*   **Data Persistence:** Utilizes SQLite for storing analysis results.

## Screenshots

Here's a glimpse of Dsam in action:

**Main Disassembly View:**
![Dsam Screenshot 1](screenshots/1.png)

**Pseudocode and Hex View:**
![Dsam Screenshot 2](screenshots/2.png)

## Getting Started

1.  Go to the [Releases](https://github.com/Saba-Developer12/Dsam/releases) page.
2.  Download the latest `Dsam.zip` file.
3.  Extract the archive and run `Dsam.App.exe`.

## Building from Source

### Prerequisites

*   .NET 9 SDK
*   Visual Studio 2022 or later (with .NET desktop development workload)

### Steps

1.  Clone the repository:
    ```sh
    git clone https://github.com/Saba-Developer12/Dsam.git
    ```
2.  Open `Dsam.sln` in Visual Studio.
3.  Build the solution (Ctrl+Shift+B).

## License

This project is licensed under the GPL-3.0 License. See the [LICENSE](LICENSE) file for details.