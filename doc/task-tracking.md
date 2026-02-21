# Task Tracking

## Project
Lords Mobile Bot - Windows 11 desktop automation foundation

## Status Legend
- `[x]` Completed
- `[~]` In Progress
- `[ ]` Pending

## Phase 1 - Foundation
- [x] Create solution and modular project structure
- [x] Wire project references and dependency direction
- [x] Configure .NET 8 targeting
- [x] Add DI base wiring
- [x] Add Serilog logging (console + file)

## Emulator Layer (`Bot.Emulator`)
- [x] Define `IEmulatorController`
- [x] Implement `AdbService` start server
- [x] Implement connected device detection
- [x] Implement tap and swipe controls
- [x] Implement screenshot capture
- [x] Implement resolution retrieval
- [x] Implement `DeviceManager` discovery/reconnect flow

## Vision Layer (`Bot.Vision`)
- [x] Define `IImageDetector`
- [x] Implement OpenCvSharp template matching
- [x] Implement confidence threshold filtering
- [x] Return center coordinates via detection result

## Core Engine (`Bot.Core`)
- [x] Add `GameState` enum
- [x] Add `StateResolver`
- [x] Add `TaskSchedulerService`
- [x] Add `BotEngine` start/stop lifecycle
- [x] Make scheduler/task pipeline extensible for additional tasks

## Tasks (`Bot.Tasks`)
- [x] Define `IBotTask`
- [x] Add `ResourceGatherTask` skeleton
- [ ] Implement real resource gather gameplay logic
- [ ] Add retry/backoff and robust task failure policies

## UI (`App.UI`)
- [x] Build WinUI dashboard shell
- [x] Add Start bot button
- [x] Add Stop bot button
- [x] Add log output panel
- [x] Add status indicator
- [x] Implement MVVM with `MainViewModel`
- [x] Wire DI registrations in startup

## Tooling & Build
- [x] Add `global.json` for SDK pinning
- [x] Add WinUI Appx tooling bootstrap script
- [x] Make solution build succeed in current environment
- [~] Keep WinUI build bootstrap stable across fresh machines

## Docs
- [x] Add `README.md` with setup/build/run instructions
- [x] Add this task tracking document
- [ ] Add troubleshooting guide for ADB/OpenCV/runtime issues

## Next Recommended Tasks
- [ ] Multi-account profile model and runtime orchestration
- [ ] Emulator/account assignment UI
- [ ] Per-task telemetry and health metrics
- [ ] Task cancellation, timeout, and circuit-breaker strategy
- [ ] Integration tests for emulator and vision adapters
