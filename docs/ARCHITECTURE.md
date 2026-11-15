# Architecture

PageCollector uses MVVM with explicit constructors and small service interfaces. The core stays independent of WPF; the window composes the real adapters. No dependency injection container or MVVM framework is required.

| Component | Owns | Does not own |
| --- | --- | --- |
| `MainWindow` / XAML | Layout, bindings, result scrolling, double-click and window lifetime. | Collection decisions or settings serialization. |
| `MainViewModel` | Editable settings, command availability, status, manual/automatic workflow and cancellation. | Windows dialogs, process launching or JSON file access. |
| `RelayCommand` / `AsyncCommand` | `ICommand` execution and availability notifications; the async command prevents overlapping executions and observes failures. | Business decisions. |
| `ObservableObject` | `INotifyPropertyChanged` notifications for WPF bindings. | Application state. |
| `IDesktopServices` | Dialogs, opening files, displaying errors and dispatching UI updates. | Capture or storage. |
| `ICaptureService` | Browser resolution, engine preparation and capture. | Choosing the next value or naming saved files. |
| `ISettingsStore` | Loading and saving settings. | View state. |
| `CollectionSession` | Current value/template and accepted-count transitions. | I/O. |
| `AutomaticCollector` | Ordered attempts, title rejection, acceptance and cleanup. | WPF or browser discovery. |
| `ArchiveStore` / `ArchiveNaming` | Collision-safe storage, provenance and filename rules. | User interaction. |

These are the Command, Observer and Adapter patterns applied where they separate concrete responsibilities. Constructor injection provides test seams. `AutomaticCollector` already accepts a capture delegate, so it does not need another abstraction.

## Lifetime and thread ownership

The WPF dispatcher owns the view model and its observable results. Core code may continue on background threads; the automatic progress callback goes through `IDesktopServices.Invoke` before changing UI state. UI commands await asynchronous capture rather than blocking the dispatcher.

Starting a run validates inputs and snapshots capture/filter/naming options. Inputs stay disabled while a session exists. Manual mode exposes decisions after each capture; automatic mode proceeds until the queue is exhausted. Saving must succeed before a value counts as accepted.

Cancellation flows into engine preparation, the capture process and archive storage. The window cancels and awaits an active command before disposing the view model. Completed archives survive cancellation; temporary previews are removed at session completion. Settings use a temporary file in the same directory and a final replace operation, so a serialization/write failure does not truncate the previous file.

## Test boundaries

Core tests exercise real file I/O in isolated temporary directories. Process integration tests launch `PageCollector.CaptureFixture`, which behaves deterministically on Windows and Linux without accessing the network. View-model tests substitute only desktop/capture/settings adapters and assert observable outcomes, command availability and saved files. A Windows STA test opens the actual XAML window and checks bindings and manual-mode visibility.

Real SingleFile/browser integration remains opt-in because it depends on an installed browser and a served fixture. Neither the controlled capture executable nor a passing WPF binding test proves that an arbitrary archived website will be captured correctly.
