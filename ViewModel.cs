using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;

namespace FifteenPuzzle
{
    // ─────────────────────────────────────────────
    //  RelayCommand — lightweight ICommand impl
    // ─────────────────────────────────────────────

    /// <summary>Generic relay command that wraps delegates.</summary>
    public class RelayCommand : ICommand
    {
        private readonly Action<object?> _execute;
        private readonly Func<object?, bool>? _canExecute;

        public RelayCommand(Action<object?> execute, Func<object?, bool>? canExecute = null)
        {
            _execute = execute ?? throw new ArgumentNullException(nameof(execute));
            _canExecute = canExecute;
        }

        public event EventHandler? CanExecuteChanged
        {
            add => CommandManager.RequerySuggested += value;
            remove => CommandManager.RequerySuggested -= value;
        }

        public bool CanExecute(object? param) => _canExecute?.Invoke(param) ?? true;
        public void Execute(object? param) => _execute(param);
    }

    // ─────────────────────────────────────────────
    //  TileViewModel — wraps a Tile for the UI
    // ─────────────────────────────────────────────

    /// <summary>Observable wrapper for a single tile shown in the grid.</summary>
    public class TileViewModel : INotifyPropertyChanged
    {
        private int _value;
        private bool _isHighlighted;

        public int Value
        {
            get => _value;
            set { _value = value; OnPropertyChanged(); OnPropertyChanged(nameof(IsBlank)); OnPropertyChanged(nameof(DisplayText)); }
        }

        public bool IsBlank => Value == 0;
        public string DisplayText => IsBlank ? string.Empty : Value.ToString();

        /// <summary>Visual highlight used by the solver step animation.</summary>
        public bool IsHighlighted
        {
            get => _isHighlighted;
            set { _isHighlighted = value; OnPropertyChanged(); }
        }

        public TileViewModel(int value) => _value = value;

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    // ─────────────────────────────────────────────
    //  MainViewModel
    // ─────────────────────────────────────────────

    /// <summary>
    /// Primary ViewModel.  Orchestrates the Board model, tile observables,
    /// move counter, persistence, and the A* auto-solver.
    /// </summary>
    public class MainViewModel : INotifyPropertyChanged
    {
        // ── Constants ───────────────────────────────────────────────────────
        private static readonly string SavePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "game_data.json");
        private static readonly JsonSerializerOptions JsonOpts =
            new() { WriteIndented = true };

        // ── Fields ──────────────────────────────────────────────────────────
        private readonly Board _board;
        private int _moveCount;
        private int _selectedSize = 4;
        private bool _isVictory;
        private bool _isSolverRunning;
        private string _statusMessage = string.Empty;
        private CancellationTokenSource? _solverCts;
        private SaveFile _saveFile = new();

        // ── Observable tile collection bound to UniformGrid ─────────────────
        public ObservableCollection<TileViewModel> TileVMs { get; } = new();

        // ── Properties ──────────────────────────────────────────────────────
        public int MoveCount
        {
            get => _moveCount;
            private set { _moveCount = value; OnPropertyChanged(); }
        }

        public int SelectedSize
        {
            get => _selectedSize;
            set
            {
                if (_selectedSize == value) return;
                _selectedSize = value;
                OnPropertyChanged();
                StartNewGame();
            }
        }

        public bool IsVictory
        {
            get => _isVictory;
            private set { _isVictory = value; OnPropertyChanged(); }
        }

        public bool IsSolverRunning
        {
            get => _isSolverRunning;
            private set { _isSolverRunning = value; OnPropertyChanged(); OnPropertyChanged(nameof(CanInteract)); }
        }

        /// <summary>False while the solver animates — prevents manual moves.</summary>
        public bool CanInteract => !IsSolverRunning;

        public string StatusMessage
        {
            get => _statusMessage;
            private set { _statusMessage = value; OnPropertyChanged(); }
        }

        public int BestResult
        {
            get
            {
                string key = _selectedSize.ToString();
                return _saveFile.BestResults.TryGetValue(key, out int v) ? v : 0;
            }
        }

        public List<int> AvailableSizes { get; } = new() { 3, 4, 5 };

        // ── Commands ────────────────────────────────────────────────────────
        public ICommand ShowRulesCommand { get; }
        public ICommand NewGameCommand { get; }
        public ICommand MoveTileCommand { get; }
        public ICommand SaveGameCommand { get; }
        public ICommand LoadGameCommand { get; }
        public ICommand SolveCommand { get; }
        public ICommand StopSolverCommand { get; }

        // ── Constructor ─────────────────────────────────────────────────────
        // ── Constructor ─────────────────────────────────────────────────────
        public MainViewModel()
        {
            _board = new Board(_selectedSize);

            ShowRulesCommand = new RelayCommand(_ => ShowRules());
            NewGameCommand = new RelayCommand(_ => StartNewGame());
            MoveTileCommand = new RelayCommand(p => OnMoveTile(p), p => CanInteract);
            SaveGameCommand = new RelayCommand(_ => SaveGame());
            LoadGameCommand = new RelayCommand(_ => LoadGame());
            SolveCommand = new RelayCommand(_ => RunSolver(), _ => !IsSolverRunning && !IsVictory);
            StopSolverCommand = new RelayCommand(_ => StopSolver(), _ => IsSolverRunning);

            LoadSaveFile();

            // Restore the saved game if it exists; otherwise, initialize a new game.
            if (_saveFile.CurrentGame != null)
            {
                _board.LoadState(_saveFile.CurrentGame);
                _selectedSize = _board.Size;
                MoveCount = _saveFile.CurrentMoves;
                SyncTileVMs();
                OnPropertyChanged(nameof(SelectedSize));
                OnPropertyChanged(nameof(BestResult));
                StatusMessage = "Game auto-loaded.";
            }
            else
            {
                StartNewGame();
            }
        }

        // ─────────────────────────────────────────────────────────────────────
        //  Game management
        // ─────────────────────────────────────────────────────────────────────

        /// <summary>Resets the board, shuffles, resets counter, rebuilds tile VMs.</summary>
        public void StartNewGame()
        {
            StopSolver();
            _board.Resize(_selectedSize);
            MoveCount = 0;
            IsVictory = false;
            StatusMessage = string.Empty;
            SyncTileVMs();
            OnPropertyChanged(nameof(BestResult));
        }

        /// <summary>Handle a tile move triggered from the UI.</summary>
        private void OnMoveTile(object? param)
        {
            if (!CanInteract) return;
            if (param is not int tileValue) return;
            if (IsVictory) return;

            if (_board.TryMove(tileValue))
            {
                MoveCount++;
                SyncTileVMs();
                CheckVictory();
            }
            // Invalid moves are silently ignored — no exception, no disruption.
        }

        private void CheckVictory()
        {
            if (!_board.IsSolved) return;

            IsVictory = true;
            UpdateBestResult();
            SaveGame();

            string best = BestResult > 0 ? $"\nBest: {BestResult} moves" : string.Empty;
            MessageBox.Show(
                $"🎉 Congratulations! Solved in {MoveCount} moves!{best}",
                "Puzzle Solved",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }

        // ── Sync observable tiles from board model ───────────────────────────
        private void SyncTileVMs()
        {
            int size = _board.Size;
            int total = size * size;

            // Rebuild collection if size changed.
            if (TileVMs.Count != total)
            {
                TileVMs.Clear();
                for (int i = 0; i < total; i++)
                    TileVMs.Add(new TileViewModel(0));
            }

            int idx = 0;
            for (int r = 0; r < size; r++)
                for (int c = 0; c < size; c++)
                    TileVMs[idx++].Value = _board.Tiles[r, c].Value;
        }

        // ─────────────────────────────────────────────────────────────────────
        //  Persistence
        // ─────────────────────────────────────────────────────────────────────
        private void ShowRules()
        {
                string rules = "How to Play:\n\n" +
                   "1. The goal is to arrange tiles from 1 to 15 (or 8/24 depending on size) starting from the top-left.\n" +
                   "2. Click a tile adjacent to the empty space to move it.\n" +
                   "3. Use 'New Game' to shuffle.\n" +
                   "4. 'Solve' button will show you the path to victory.\n" +
                   "5. Your best results are saved automatically!";

                MessageBox.Show(rules, "Game Rules & Instructions", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        private void LoadSaveFile()
        {
            try
            {
                if (!File.Exists(SavePath)) return;
                string json = File.ReadAllText(SavePath);
                _saveFile = JsonSerializer.Deserialize<SaveFile>(json, JsonOpts) ?? new SaveFile();
            }
            catch (Exception ex)
            {
                StatusMessage = $"Could not load save file: {ex.Message}";
            }
        }

        public void SaveGame()
        {
            try
            {
                _saveFile.CurrentGame = _board.ToState();
                _saveFile.CurrentMoves = MoveCount;
                File.WriteAllText(SavePath, JsonSerializer.Serialize(_saveFile, JsonOpts));
                StatusMessage = "Game saved.";
            }
            catch (Exception ex)
            {
                StatusMessage = $"Save failed: {ex.Message}";
            }
        }

        public void LoadGame()
        {
            try
            {
                LoadSaveFile();
                if (_saveFile.CurrentGame == null)
                {
                    StatusMessage = "No saved game found.";
                    return;
                }

                StopSolver();
                _board.LoadState(_saveFile.CurrentGame);
                _selectedSize = _board.Size;
                OnPropertyChanged(nameof(SelectedSize));
                MoveCount = _saveFile.CurrentMoves;
                IsVictory = false;
                SyncTileVMs();
                OnPropertyChanged(nameof(BestResult));
                StatusMessage = "Game loaded.";
            }
            catch (Exception ex)
            {
                StatusMessage = $"Load failed: {ex.Message}";
            }
        }

        private void UpdateBestResult()
        {
            string key = _selectedSize.ToString();
            if (!_saveFile.BestResults.TryGetValue(key, out int current) || MoveCount < current)
                _saveFile.BestResults[key] = MoveCount;
            OnPropertyChanged(nameof(BestResult));
        }

        // ─────────────────────────────────────────────────────────────────────
        //  A* Auto-Solver (async, step-by-step animation)
        // ─────────────────────────────────────────────────────────────────────

        private async void RunSolver()
        {
            if (_board.Size > 4)
            {
                MessageBox.Show(
                    "Auto-solve is available for 3×3 and 4×4 grids only.\n" +
                    "5×5 grids have too many states for real-time solving.",
                    "Solver Limitation",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

            StopSolver(); // cancel any previous run
            _solverCts = new CancellationTokenSource();
            var token = _solverCts.Token;

            IsSolverRunning = true;
            StatusMessage = "Solving…";

            // Run the A* search on a background thread so the UI stays responsive.
            int[] snapshot = _board.ToFlatArray();
            int size = _board.Size;

            List<int>? moves = null;
            try
            {
                moves = await Task.Run(() => PuzzleSolver.Solve(snapshot, size), token);
            }
            catch (OperationCanceledException) { }

            if (token.IsCancellationRequested)
            {
                IsSolverRunning = false;
                StatusMessage = "Solver stopped.";
                return;
            }

            if (moves == null)
            {
                IsSolverRunning = false;
                StatusMessage = "Solver exceeded node limit.";
                return;
            }

            StatusMessage = $"Solution found: {moves.Count} moves. Animating…";

            // Animate each move with a short delay.
            foreach (int tileValue in moves)
            {
                if (token.IsCancellationRequested) break;

                // Highlight the tile about to move.
                HighlightTile(tileValue, true);
                await Task.Delay(220, token).ContinueWith(_ => { }, TaskContinuationOptions.None);

                if (token.IsCancellationRequested) break;

                _board.TryMove(tileValue);
                MoveCount++;
                SyncTileVMs();
                HighlightTile(tileValue, false);

                await Task.Delay(120, token).ContinueWith(_ => { }, TaskContinuationOptions.None);
            }

            IsSolverRunning = false;

            if (!token.IsCancellationRequested)
            {
                StatusMessage = "Solved!";
                CheckVictory();
            }
            else
            {
                StatusMessage = "Solver stopped.";
            }
        }

        private void StopSolver()
        {
            _solverCts?.Cancel();
            _solverCts = null;
            IsSolverRunning = false;
        }

        private void HighlightTile(int value, bool on)
        {
            foreach (var vm in TileVMs)
                if (vm.Value == value) { vm.IsHighlighted = on; return; }
        }

        // ── INotifyPropertyChanged ───────────────────────────────────────────
        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
