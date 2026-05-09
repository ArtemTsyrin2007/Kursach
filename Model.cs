using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Serialization;

namespace FifteenPuzzle
{
    // ─────────────────────────────────────────────
    //  Interfaces
    // ─────────────────────────────────────────────

    /// <summary>Defines the contract for any n×n sliding-puzzle board.</summary>
    public interface IBoard
    {
        int Size { get; }
        Tile[,] Tiles { get; }
        bool IsSolved { get; }
        bool TryMove(int tileValue);
        void Shuffle();
    }

    /// <summary>Defines state that can be serialised/deserialised.</summary>
    public interface IPersistable
    {
        GameState ToState();
        void LoadState(GameState state);
    }

    // ─────────────────────────────────────────────
    //  Tile
    // ─────────────────────────────────────────────

    /// <summary>Represents a single tile on the board. Value 0 = blank.</summary>
    public class Tile
    {
        public int Value { get; set; }
        public bool IsBlank => Value == 0;

        public Tile(int value) => Value = value;

        // Deep-copy constructor used by the solver.
        public Tile Clone() => new(Value);
    }

    // ─────────────────────────────────────────────
    //  Board
    // ─────────────────────────────────────────────

    /// <summary>
    /// Core game-logic class.  Handles shuffling (guaranteed solvable via
    /// inversion-parity), move validation, and win detection.
    /// </summary>
    public class Board : IBoard, IPersistable
    {
        private readonly Random _rng = new();

        public int Size { get; private set; }
        public Tile[,] Tiles { get; private set; }

        // Position of the blank tile (row, col).
        public int BlankRow { get; private set; }
        public int BlankCol { get; private set; }

        public bool IsSolved
        {
            get
            {
                int expected = 1;
                for (int r = 0; r < Size; r++)
                    for (int c = 0; c < Size; c++)
                    {
                        int v = Tiles[r, c].Value;
                        if (r == Size - 1 && c == Size - 1) return v == 0;
                        if (v != expected++) return false;
                    }
                return true;
            }
        }

        public Board(int size = 4)
        {
            Size = size;
            Tiles = new Tile[size, size];
            InitSolved();
        }

        // ── Initialise in solved order ──────────────────────────────────────
        private void InitSolved()
        {
            int n = 1;
            for (int r = 0; r < Size; r++)
                for (int c = 0; c < Size; c++)
                {
                    int v = (r == Size - 1 && c == Size - 1) ? 0 : n++;
                    Tiles[r, c] = new Tile(v);
                }
            BlankRow = Size - 1;
            BlankCol = Size - 1;
        }

        // ── Resize and shuffle ──────────────────────────────────────────────
        public void Resize(int newSize)
        {
            Size = newSize;
            Tiles = new Tile[newSize, newSize];
            InitSolved();
            Shuffle();
        }

        // ── Shuffle using Fisher-Yates, then fix parity ─────────────────────
        public void Shuffle()
        {
            InitSolved();

            // Flatten to 1-D (excluding blank), shuffle, then re-fill.
            int total = Size * Size;
            int[] vals = Enumerable.Range(0, total).ToArray();

            for (int i = total - 1; i > 0; i--)
            {
                int j = _rng.Next(i + 1);
                (vals[i], vals[j]) = (vals[j], vals[i]);
            }

            // Ensure solvability (even-inversion parity for even-size boards,
            // combined with blank-row distance for odd-size boards).
            if (!IsSolvableFlat(vals, Size))
                SwapFirstTwoNonBlanks(vals); // single swap corrects parity

            // Write back to 2-D grid.
            int idx = 0;
            for (int r = 0; r < Size; r++)
                for (int c = 0; c < Size; c++)
                {
                    Tiles[r, c].Value = vals[idx];
                    if (vals[idx] == 0) { BlankRow = r; BlankCol = c; }
                    idx++;
                }
        }

        // ── Move tile adjacent to blank ─────────────────────────────────────
        /// <summary>
        /// Moves the tile with the given value into the blank space.
        /// Returns false if the tile is not adjacent to the blank.
        /// </summary>
        public bool TryMove(int tileValue)
        {
            var (tr, tc) = FindTile(tileValue);
            if (tr < 0) return false; // tile not found

            bool adjacent = (Math.Abs(tr - BlankRow) + Math.Abs(tc - BlankCol)) == 1;
            if (!adjacent) return false;

            // Swap tile with blank.
            Tiles[BlankRow, BlankCol].Value = tileValue;
            Tiles[tr, tc].Value = 0;
            BlankRow = tr;
            BlankCol = tc;
            return true;
        }

        // ── Locate a tile by value ──────────────────────────────────────────
        public (int row, int col) FindTile(int value)
        {
            for (int r = 0; r < Size; r++)
                for (int c = 0; c < Size; c++)
                    if (Tiles[r, c].Value == value) return (r, c);
            return (-1, -1);
        }

        // ─────────────────────────────────────────────────────────────────────
        //  Solvability check (inversion counting)
        //  Reference: https://www.cs.bham.ac.uk/~mdr/teaching/modules04/
        // ─────────────────────────────────────────────────────────────────────
        private static bool IsSolvableFlat(int[] arr, int size)
        {
            // Count inversions (pairs where a larger number precedes a smaller).
            int inversions = 0;
            for (int i = 0; i < arr.Length - 1; i++)
                for (int j = i + 1; j < arr.Length; j++)
                    if (arr[i] != 0 && arr[j] != 0 && arr[i] > arr[j])
                        inversions++;

            if (size % 2 == 1) // Odd grid: solvable iff inversions even.
                return inversions % 2 == 0;

           // Even grid: solvable iff (inversions + blank-row-from-bottom) is odd.
            int blankIdx = Array.IndexOf(arr, 0);
            int blankRowFromBottom = size - blankIdx / size;
            return (inversions + blankRowFromBottom) % 2 != 0;
        }

        private static void SwapFirstTwoNonBlanks(int[] arr)
        {
            int first = -1;
            for (int i = 0; i < arr.Length; i++)
            {
                if (arr[i] == 0) continue;
                if (first == -1) { first = i; }
                else { (arr[first], arr[i]) = (arr[i], arr[first]); return; }
            }
        }

        // ── IPersistable ────────────────────────────────────────────────────
        public GameState ToState()
        {
            int total = Size * Size;
            int[] flat = new int[total];
            int idx = 0;
            for (int r = 0; r < Size; r++)
                for (int c = 0; c < Size; c++)
                    flat[idx++] = Tiles[r, c].Value;
            return new GameState { Size = Size, Tiles = flat };
        }

        public void LoadState(GameState state)
        {
            Size = state.Size;
            Tiles = new Tile[Size, Size];
            int idx = 0;
            for (int r = 0; r < Size; r++)
                for (int c = 0; c < Size; c++)
                {
                    Tiles[r, c] = new Tile(state.Tiles[idx]);
                    if (state.Tiles[idx] == 0) { BlankRow = r; BlankCol = c; }
                    idx++;
                }
        }

        // ── Flat board snapshot (used by solver) ────────────────────────────
        public int[] ToFlatArray()
        {
            int[] arr = new int[Size * Size];
            int idx = 0;
            for (int r = 0; r < Size; r++)
                for (int c = 0; c < Size; c++)
                    arr[idx++] = Tiles[r, c].Value;
            return arr;
        }
    }

    // ─────────────────────────────────────────────
    //  Persistence models
    // ─────────────────────────────────────────────

    /// <summary>Serialisable snapshot of a board position.</summary>
    public class GameState
    {
        [JsonPropertyName("size")]
        public int Size { get; set; }

        [JsonPropertyName("tiles")]
        public int[] Tiles { get; set; } = Array.Empty<int>();
    }

    /// <summary>Root JSON document written to game_data.json.</summary>
    public class SaveFile
    {
        [JsonPropertyName("currentGame")]
        public GameState? CurrentGame { get; set; }

        [JsonPropertyName("currentMoves")]
        public int CurrentMoves { get; set; }

        /// <summary>Best (lowest) move-count per grid size, keyed by size string.</summary>
        [JsonPropertyName("bestResults")]
        public Dictionary<string, int> BestResults { get; set; } = new();
    }

    // ─────────────────────────────────────────────
    //  A* Solver
    // ─────────────────────────────────────────────

    /// <summary>
    /// A* solver using the Manhattan-distance heuristic.
    /// Returns an ordered list of tile values to move, or null if no
    /// solution is found within the node limit (avoids UI freeze on large grids).
    /// </summary>
    public static class PuzzleSolver
    {
        private const int MaxNodes = 1_000_000; // safety cap

        public static List<int>? Solve(int[] startFlat, int size)
        {
            // Build goal state.
            int total = size * size;
            int[] goal = new int[total];
            for (int i = 0; i < total - 1; i++) goal[i] = i + 1;
            goal[total - 1] = 0;

            if (startFlat.SequenceEqual(goal)) return new List<int>();

            var startNode = new SolverNode(startFlat, size, null, -1, 0);
            var open = new SortedSet<SolverNode>(SolverNode.FComparer);
            var visited = new HashSet<string>();

            open.Add(startNode);

            while (open.Count > 0 && visited.Count < MaxNodes)
            {
                var current = open.Min!;
                open.Remove(current);

                string key = string.Join(",", current.State);
                if (!visited.Add(key)) continue;

                if (current.State.SequenceEqual(goal))
                    return ReconstructPath(current);

                foreach (var child in current.Expand(size))
                {
                    string ck = string.Join(",", child.State);
                    if (!visited.Contains(ck))
                        open.Add(child);
                }
            }

            return null; // exceeded node limit
        }

        private static List<int> ReconstructPath(SolverNode node)
        {
            var path = new List<int>();
            var cur = node;
            while (cur.Parent != null)
            {
                path.Add(cur.MovedTile);
                cur = cur.Parent;
            }
            path.Reverse();
            return path;
        }
    }

    /// <summary>Single node in the A* search tree.</summary>
    internal class SolverNode
    {
        public int[] State { get; }
        public SolverNode? Parent { get; }
        public int MovedTile { get; }   // tile value that was moved to reach this node
        public int G { get; }           // cost from start
        public int H { get; }           // Manhattan heuristic
        public int F => G + 10*H;

        private readonly int _size;

        public SolverNode(int[] state, int size, SolverNode? parent, int movedTile, int g)
        {
            State = state;
            _size = size;
            Parent = parent;
            MovedTile = movedTile;
            G = g;
            H = CalculateHeuristic(state, size);
        }
// ── Enhanced Heuristic: Manhattan + Linear Conflict ─────────────────
private static int CalculateHeuristic(int[] state, int size)
{
    int dist = 0;
    int linearConflict = 0;

    for (int i = 0; i < state.Length; i++)
    {
        int v = state[i];
        if (v == 0) continue;

        int targetIdx = v - 1; // goal index of value v
        int curRow = i / size, curCol = i % size;
        int tgtRow = targetIdx / size, tgtCol = targetIdx % size;

        dist += Math.Abs(curRow - tgtRow) + Math.Abs(curCol - tgtCol);

        if (curRow == tgtRow)
        {
            for (int k = i + 1; k < curRow * size + size; k++)
            {
                int v2 = state[k];
                // Если v2 тоже на своей целевой строке, но должна стоять левее v
                if (v2 != 0 && (v2 - 1) / size == curRow && v > v2)
                    linearConflict += 2;
            }
        }

        if (curCol == tgtCol)
        {
            for (int k = i + size; k < state.Length; k += size)
            {
                int v2 = state[k];
                if (v2 != 0 && (v2 - 1) % size == curCol && v > v2)
                    linearConflict += 2;
            }
        }
    }
    return dist + linearConflict;
}
        // ── Generate children by sliding each neighbour of blank ─────────────
        public IEnumerable<SolverNode> Expand(int size)
        {
            int blankIdx = Array.IndexOf(State, 0);
            int br = blankIdx / size, bc = blankIdx % size;

            int[][] dirs = { new[] { -1, 0 }, new[] { 1, 0 }, new[] { 0, -1 }, new[] { 0, 1 } };
            foreach (var d in dirs)
            {
                int nr = br + d[0], nc = bc + d[1];
                if (nr < 0 || nr >= size || nc < 0 || nc >= size) continue;

                int nIdx = nr * size + nc;
                int[] newState = (int[])State.Clone();
                (newState[blankIdx], newState[nIdx]) = (newState[nIdx], newState[blankIdx]);

                yield return new SolverNode(newState, size, this, newState[blankIdx], G + 1);
            }
        }

        // ── Comparer for SortedSet (by F, then by unique hash to avoid duplicates) ──
        public static readonly IComparer<SolverNode> FComparer =
            Comparer<SolverNode>.Create((a, b) =>
            {
                int cmp = a.F.CompareTo(b.F);
                if (cmp != 0) return cmp;
                cmp = a.G.CompareTo(b.G);
                if (cmp != 0) return cmp;
                // Tie-break by state hash so equal-F nodes co-exist in the set.
                return string.Compare(
                    string.Join(",", a.State),
                    string.Join(",", b.State),
                    StringComparison.Ordinal);
            });
    }
}
