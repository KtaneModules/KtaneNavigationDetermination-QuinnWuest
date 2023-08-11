using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEngine;
using Rnd = UnityEngine.Random;
using KModkit;

public class NavigationDeterminationScript : MonoBehaviour
{
    public KMBombModule Module;
    public KMBombInfo BombInfo;
    public KMAudio Audio;
    public KMRuleSeedable RuleSeedable;

    public Material[] ButtonMats;
    public Color32[] TextColors;
    public KMSelectable[] ButtonSels;
    public KMSelectable DisplaySel;
    public MeshRenderer[] ButtonRends;
    public TextMesh[] ButtonTexts;
    public GameObject ArrowObj;
    public Material CheckMat;

    private int _moduleId;
    private static int _moduleIdCounter = 1;
    private bool _moduleSolved;

    private MazeGenerator _mazeGenerator;

    public class NavDetMaze
    {
        public string MazeWalls { get; private set; }
        public int Color { get; private set; }
        public char Label { get; private set; }
        public NavDetMaze(string mazeWalls, int color, char label)
        {
            MazeWalls = mazeWalls;
            Color = color;
            Label = label;
        }
    }
    private NavDetMaze[] _mazes = new NavDetMaze[16];

    public class NavDetPath
    {
        public int[] Path { get; private set; }
        public int MazeNum { get; private set; }
        public char SnChar { get; private set; }

        public NavDetPath(int[] path, int mazeNum, char snChar)
        {
            Path = path;
            MazeNum = mazeNum;
            SnChar = snChar;
        }
    }

    private NavDetPath _chosenPath;
    private static readonly string[] _colorNames = new string[4] { "Red", "Yellow", "Green", "Blue" };
    private int? _currentlyPointedAtColor;
    private bool _isAnimating;
    private int[] _btnColors;
    private char[] _btnLetters;
    private Coroutine _displayCoroutine;

    private void Start()
    {
        _moduleId = _moduleIdCounter++;

        // START RULE SEED
        var rnd = RuleSeedable.GetRNG();
        _mazeGenerator = new MazeGenerator(7, rnd);
        for (int i = 0; i < 16; i++)
        {
            var chars = "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZ".ToArray();
            rnd.ShuffleFisherYates(chars);
            var maze = _mazeGenerator.GenerateMaze().ToCharArray();
            var ixs = new int[] { 16, 18, 20, 24, 26, 28, 46, 48, 50, 54, 56, 58, 76, 78, 80, 84, 86, 88, 136, 138, 140, 144, 146, 148, 166, 168, 170, 174, 176, 178, 196, 198, 200, 204, 206, 208 };
            for (int ix = 0; ix < ixs.Length; ix++)
                maze[ixs[ix]] = chars[ix];
            _mazes[i] = new NavDetMaze(maze.Join(""), i % 4, "ABCD"[i / 4]);
        }
        // END RULE SEED

        var serNum = BombInfo.GetSerialNumber().Distinct().ToArray();
        var listOfPaths = new List<NavDetPath>();
        for (int mazeNum = 0; mazeNum < _mazes.Length; mazeNum++)
        {
            for (int sn = 0; sn < serNum.Length; sn++)
            {
                var path = RemoveConsecutive(FindPath(mazeNum, serNum[sn])).ToArray();
                listOfPaths.Add(new NavDetPath(path, mazeNum, serNum[sn]));
            }
        }

        var duplicatesRemoved = listOfPaths.Where(ndpa => listOfPaths.Count(ndpb => ndpb.Path.SequenceEqual(ndpa.Path)) == 1).ToArray();
        _chosenPath = duplicatesRemoved.PickRandom();
        Debug.LogFormat("[Navigation Determination #{0}] Chosen maze: Color {1}, Label {2}.", _moduleId, _colorNames[_mazes[_chosenPath.MazeNum].Color], _mazes[_chosenPath.MazeNum].Label);
        Debug.LogFormat("[Navigation Determination #{0}] Target: {1}.", _moduleId, _chosenPath.SnChar);
        Debug.LogFormat("[Navigation Determination #{0}] Path: {1}", _moduleId, _chosenPath.Path.Select(i => "URDL"[i]).Join(" "));

        var p = _chosenPath.Path.PickRandom();
        _btnColors = Enumerable.Range(0, 4).ToArray().Shuffle();
        while (_btnColors[p] != _mazes[_chosenPath.MazeNum].Color)
            _btnColors.Shuffle();
        for (int i = 0; i < 4; i++)
        {
            ButtonRends[i].sharedMaterial = ButtonMats[_btnColors[i]];
            ButtonTexts[i].color = TextColors[_btnColors[i]];
        }
        _btnLetters = "ABCD".ToArray().Shuffle();
        for (int i = 0; i < 4; i++)
            ButtonTexts[i].text = _btnLetters[i].ToString();

        ArrowObj.SetActive(false);
        for (int i = 0; i < 4; i++)
            ButtonSels[i].OnInteract += ButtonPress(i);
        DisplaySel.OnInteract += DisplayPress;
    }

    private KMSelectable.OnInteractHandler ButtonPress(int i)
    {
        return delegate ()
        {
            ButtonSels[i].AddInteractionPunch(0.5f);
            Audio.PlayGameSoundAtTransform(KMSoundOverride.SoundEffect.ButtonPress, ButtonSels[i].transform);
            if (_moduleSolved)
                return false;
            if (_currentlyPointedAtColor != _mazes[_chosenPath.MazeNum].Color || _btnLetters[i] != _mazes[_chosenPath.MazeNum].Label)
            {
                Module.HandleStrike();
                Debug.LogFormat("[Navigation Determination #{0}] Incorrectly pressed {1} when the arrow was pointing at {2}. Strike.", _moduleId, _btnLetters[i], _currentlyPointedAtColor == null ? "nothing" : _colorNames[_currentlyPointedAtColor.Value]);
            }
            else
            {
                _moduleSolved = true;
                Module.HandlePass();
                Debug.LogFormat("[Navigation Determination #{0}] Correctly pressed {1} when the arrow was pointing at {2}. Module solved.", _moduleId, _btnLetters[i], _currentlyPointedAtColor == null ? "nothing" : _colorNames[_currentlyPointedAtColor.Value]);
                if (_displayCoroutine != null)
                    StopCoroutine(_displayCoroutine);
                ArrowObj.GetComponent<MeshRenderer>().sharedMaterial = CheckMat;
                ArrowObj.transform.localEulerAngles = new Vector3(90f, 0f, 0f);
                Audio.PlayGameSoundAtTransform(KMSoundOverride.SoundEffect.CorrectChime, transform);
            }
            return false;
        };
    }

    private bool DisplayPress()
    {
        DisplaySel.AddInteractionPunch(0.5f);
        Audio.PlayGameSoundAtTransform(KMSoundOverride.SoundEffect.ButtonRelease, DisplaySel.transform);
        if (_moduleSolved || _isAnimating)
            return false;
        _displayCoroutine = StartCoroutine(PathDisplayAnimation());
        return false;
    }

    private IEnumerator PathDisplayAnimation()
    {
        _isAnimating = true;
        for (int i = 0; i < _chosenPath.Path.Length; i++)
        {
            ArrowObj.SetActive(true);
            ArrowObj.transform.localEulerAngles = new Vector3(90f, _chosenPath.Path[i] * 90f, 0f);
            _currentlyPointedAtColor = _btnColors[_chosenPath.Path[i]];
            yield return new WaitForSeconds(0.6f);
            ArrowObj.SetActive(false);
            _currentlyPointedAtColor = null;
            if (i < _chosenPath.Path.Length - 1)
                yield return new WaitForSeconds(0.2f);
        }
        _isAnimating = false;
    }

    private IEnumerable<int> RemoveConsecutive(IList<int> list)
    {
        for (int i = 0; i < list.Count; i++)
            if (i == 0 || list[i - 1] != list[i])
                yield return list[i];
    }

    struct QueueItem
    {
        public int Cell;
        public int Parent;
        public int Direction;
        public QueueItem(int cell, int parent, int dir)
        {
            Cell = cell;
            Parent = parent;
            Direction = dir;
        }
    }

    private bool CheckMovement(int maze, int pos, int dir)
    {
        return _mazes[maze].MazeWalls[pos + GetMoveOffset(dir)] != '█';
    }

    private int GetMoveOffset(int dir)
    {
        return dir == 0 ? -1 * (7 * 2 + 1) : dir == 1 ? 1 : dir == 2 ? (7 * 2 + 1) : -1;
    }

    private IList<int> FindPath(int maze, char c)
    {
        var visited = new Dictionary<int, QueueItem>();
        var q = new Queue<QueueItem>();
        var goal = _mazes[maze].MazeWalls.IndexOf(c);
        q.Enqueue(new QueueItem(112, -1, 0));
        while (q.Count > 0)
        {
            var qi = q.Dequeue();
            if (visited.ContainsKey(qi.Cell))
                continue;
            visited[qi.Cell] = qi;
            if (qi.Cell == goal)
                break;
            if (CheckMovement(maze, qi.Cell, 0))
                q.Enqueue(new QueueItem(qi.Cell - 30, qi.Cell, 0));
            if (CheckMovement(maze, qi.Cell, 1))
                q.Enqueue(new QueueItem(qi.Cell + 2, qi.Cell, 1));
            if (CheckMovement(maze, qi.Cell, 2))
                q.Enqueue(new QueueItem(qi.Cell + 30, qi.Cell, 2));
            if (CheckMovement(maze, qi.Cell, 3))
                q.Enqueue(new QueueItem(qi.Cell - 2, qi.Cell, 3));
        }
        var r = goal;
        var path = new List<int>();
        while (true)
        {
            var nr = visited[r];
            if (nr.Parent == -1)
                break;
            path.Add(nr.Direction);
            r = nr.Parent;
        }
        path.Reverse();
        return path;
    }

#pragma warning disable 0414
    private readonly string TwitchHelpMessage = "!{0} play [Press the display to play the sequence.] | !{0} submit A green [Press A when the arrow points at green.]";
#pragma warning restore 0414

    private IEnumerator ProcessTwitchCommand(string command)
    {
        var m = Regex.Match(command, @"^\s*play\s*$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        if (m.Success)
        {
            if (_isAnimating)
            {
                yield return "sendtochaterror The sequence is already playing!";
                yield break;
            }
            yield return null;
            DisplaySel.OnInteract();
            yield break;
        }
        m = Regex.Match(command, @"^\s*(?:submit|press)\s+(?<letter>[ABCD])\s+(?<color>r|red|y|yellow|g|green|b|blue)\s*$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        if (m.Success)
        {
            var letter = m.Groups["letter"].Value.ToLowerInvariant();
            var color = "rygb".IndexOf(m.Groups["color"].Value.ToLowerInvariant()[0]);
            while (_isAnimating)
            {
                yield return null;
                if (_currentlyPointedAtColor == color)
                {
                    ButtonSels[Array.IndexOf(_btnLetters, letter)].OnInteract();
                    yield break;
                }
            }
            DisplaySel.OnInteract();
            yield return new WaitForSeconds(0.1f);
            while (_isAnimating)
            {
                yield return null;
                if (_currentlyPointedAtColor == color)
                {
                    ButtonSels[Array.IndexOf(_btnLetters, letter)].OnInteract();
                    yield break;
                }
            }
            yield return "sendtochaterror The given submission is not valid.";
            yield return "unsubmittablepenalty 2";
        }
    }

    private IEnumerator TwitchHandleForcedSolve()
    {
        while (_isAnimating)
        {
            yield return true;
            if (_currentlyPointedAtColor == _mazes[_chosenPath.MazeNum].Color)
            {
                ButtonSels[Array.IndexOf(_btnLetters, _mazes[_chosenPath.MazeNum].Label)].OnInteract();
                yield break;
            }
        }
        DisplaySel.OnInteract();
        yield return new WaitForSeconds(0.1f);
        while (_isAnimating)
        {
            yield return null;
            if (_currentlyPointedAtColor == _mazes[_chosenPath.MazeNum].Color)
            {
                ButtonSels[Array.IndexOf(_btnLetters, _mazes[_chosenPath.MazeNum].Label)].OnInteract();
                yield break;
            }
        }
    }
}