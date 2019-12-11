using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Principal;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Xml;
using Item = Compiler.LinkList<Compiler.Parser.LR1Production>;

namespace Compiler
{
    class Program
    {
        static void Main(string[] args)
        {
            string s = "S-L=R|R L-*R|x R-L";
            //E-TE' E'-+TE'|0 T-FT' T'-*FT'|0 F-(E)|a
            //L-E E-E+T|E=T|T T-T*F|T/F|F F-(E)|x
            //L-E E-TE' E'-+TE'|=TE'|0 T-FT' T'-*FT'|/FT'|0 F-(E)|x
            //S-L=R|R L-*R|x R-L

            Parser parser = new Parser {Src = s, Input = "aaa"};
            parser.Excute();
            //parser.Print();
        }
    }

    internal static class Extension
    {
        public static void Append(this HashSet<string> h1, HashSet<string> h2)
        {
            foreach (string s in h2)
            {
                h1.Add(s);
            }
        }

        public static void Append(this HashSet<string> h1, List<string> h2)
        {
            foreach (string s in h2)
            {
                h1.Add(s);
            }
        }

        public static void AppendWithoutNull(this HashSet<string> h1, HashSet<string> h2)
        {
            foreach (string s in h2)
            {
                if (s != "0")
                {
                    h1.Add(s);
                }
            }
        }

        public static List<string> Merge(this List<string> list1, List<string> list2)
        {
            var result= list1.ToList();
            result.AddRange(list2);

            return result;
        }

        public static bool Equal<T>(this HashSet<T> s1, HashSet<T> s2)
        {
            return s1.All(s2.Contains) && s2.All(s1.Contains);
        }
    }

    internal static class Util
    {
        public static bool IsLetter(char c)
        {
            if (c >= 'a' && c <= 'z' || c >= 'A' && c <= 'Z')
                return true;
            return false;
        }

        public static bool IsDigit(char c)
        {
            if (c >= '0' && c <= '9')
                return true;
            return false;
        }

        public static string GetChar(string s, int i = 0)
        {
            string c = s[i].ToString();

            if (i != s.Length - 1 && s[i + 1] == '\'')
            {
                c = c + "\'";
            }

            return c;
        }

        public static List<string> GetChars(string s, int i = 0)
        {
            var list = new List<string>();

            for (; i < s.Length; i++)
            {
                var c = GetChar(s, i);

                list.Add(c);

                if (c.Length > 1)
                {
                    i++;
                }
            }

            return list;
        }
    }

    internal class LinkList<T>
    {
        internal class Node<T>
        {
            public T       Element;
            public Node<T> Next;
        }

        private Node<T> _first;
        private Node<T> _last;
        private Node<T> _current;
        private int     _count;
        private int     _currentNum;
        
        public LinkList()
        {
            _first = _current = _last = new Node<T>();
            _count = 0;
            _currentNum = -1;
        }

        public void Add(T element)
        {
            _last.Next    = new Node<T>();
            _last         = _last.Next;
            _last.Element = element;
            _count++;
        }

        public int Count()
        {
            return _count;
        }

        public T First()
        {
            return _first.Next.Element;
        }

        public T Current()
        {
            return _current.Element;
        }

        public Node<T> CurrentPos()
        {
            return _current;
        }

        public bool TryMove()
        {
            if (_last != _current)
            {
                _current = _current.Next;
                _currentNum++;
                return true;
            }

            return false;
        }

        public void Reset()
        {
            _current = _first;
            _currentNum = -1;
        }

        public int CurrentNum()
        {
            return _currentNum;
        }

        public override bool Equals(object obj)
        {
            if (!(obj is LinkList<T> linkList))
                return false;
            
            if (linkList._count != _count)
                return false;
            
            var objCurrent = linkList._current;
            var thisCurrent = _current;
            
            linkList.Reset();
            Reset();
            
            while (linkList.TryMove() && TryMove())
            {
                var left = Current();
                var right = linkList.Current();
                if (!left.Equals(right))
                {
                    linkList._current = objCurrent;
                    _current = thisCurrent;
                    return false;
                }
            }

            linkList._current = objCurrent;
            _current          = thisCurrent;
            
            return true;
        }

        public T IteForEqual(T e)
        {
            var current = _current;
            Reset();
            while (TryMove())
            {
                var e1 = Current();
                if (!e1.Equals(e)) continue;
                _current = current;
                return e;
            }
            _current = current;
            return default;
        }
    }

    internal class LexicalAnalyzer
    {
        internal class KeyValue
        {
            public string Key;
            public string Value;

            public KeyValue(string key, string value)
            {
                this.Key   = key;
                this.Value = value;
            }
        }

        //关键字表
        private static string[] keyWords =
        {
            "auto", "break", "case", "char", "const", "continue",
            "default", "do", "double", "else", "enum", "extern",
            "float", "for", "goto", "if", "int", "long",
            "register", "return", "short", "signed", "sizeof", "static",
            "struct", "switch", "typedef", "union", "unsigned", "void",
            "volatile", "while"
        };

        private static string[] op =
        {
            "+", "-", "*", "/", "<", "<=", ">", ">=", "=", "==",
            "!=", ";", "(", ")", "^", ",", "\"", "\'", "#", "&",
            "&&", "|", "||", "%", "~", "<<", ">>", "[", "]", "{",
            "}", "\\", ".", "?", ":", "!"
        };

        string source;           //源文件代码
        char   endSymbol = '\r'; //结束标志
        int    offset    = 0;    //源代码指针

        StringBuilder token = new StringBuilder();

        public List<KeyValue> result = new List<KeyValue>();

        public LexicalAnalyzer(string source)
        {
            this.source = source;
        }

        bool IsLetterOrLine(char head)
        {
            if (Util.IsLetter(head) || head == '_')
                return true;
            return false;
        }

        public void DelAnnotation()
        {
            if (++offset == source.Length - 1)
                throw new Exception("注释格式错误：single \"/\"");

            if (source[offset] == '/')
            {
                do
                {
                    if (++offset == source.Length - 1)
                        throw new Exception("注释格式错误：single \"/\"");
                } while (source[offset] != '\n');

                offset++;
                return;
            }

            if (source[offset] == '*')
            {
                do
                {
                    if (++offset == source.Length - 1)
                        throw new Exception("注释格式错误：missing */");
                } while (source[offset] != '*');

                if (++offset == source.Length - 1 || source[++offset] != '/')
                    throw new Exception("注释格式错误：missing /");
                return;
            }

            throw new Exception("注释格式错误：single \"/\"");
        }

        //Two-Operator
        private void TwoOp(char first, params char[] list)
        {
            offset++;

            foreach (var second in list)
            {
                if (source[offset] == second)
                {
                    var o2p = new StringBuilder().Append(first).Append(second).ToString();
                    result.Add(new KeyValue(o2p, Array.IndexOf(op, o2p).ToString()));
                    goto end;
                }
            }

            offset--;
            result.Add(new KeyValue(first.ToString(), Array.IndexOf(op, first.ToString()).ToString()));

            end :
            offset++;
        }

        public void Scanner()
        {
            while (true)
            {
                var head = source[offset];
                if (IsLetterOrLine(head))
                {
                    //开头是字母或_
                    while (Util.IsDigit(source[offset]) || IsLetterOrLine(source[offset]))
                    {
                        token.Append(source[offset++]);
                    }

                    int num;
                    var tmp = new KeyValue(token.ToString(),
                                           (num = Array.IndexOf(keyWords, token.ToString())) != -1
                                               ? num.ToString()
                                               : "Identifier");
                    result.Add(tmp);

                    token.Clear();
                }
                else if (Util.IsDigit(head))
                {
                    //开头是数字
                    while (Util.IsDigit(source[offset]))
                    {
                        token.Append(source[offset++]);
                    }

                    var tmp = new KeyValue(token.ToString(), "Digit");
                    result.Add(tmp);

                    token.Clear();
                }
                else if (head == ' ')
                {
                    offset++;
                }
                else if (head == '+'  || head == '-' || head == '*' || head == '/'  || head == ';'  || head == '(' ||
                         head == ')'  || head == '^' || head == ',' || head == '\"' || head == '\'' || head == '~' ||
                         head == '#'  || head == '%' || head == '[' || head == ']'  || head == '{'  || head == '}' ||
                         head == '\\' || head == '.' || head == '?' || head == ':')
                {
                    var tmp = new KeyValue(head.ToString(), Array.IndexOf(op, head.ToString()).ToString());
                    result.Add(tmp);
                    offset++;
                }
                else if (head == '<')
                {
                    TwoOp('<', '=', '<');
                }
                else if (head == '>')
                {
                    TwoOp('>', '=', '>');
                }
                else if (head == '=')
                {
                    TwoOp('=', '=');
                }
                else if (source[offset] == endSymbol)
                {
                    return;
                }
                else
                {
                    Console.WriteLine("scanner_error");
                    return;
                }
            }
        }
    }

    internal class Parser
    {
        internal class Vertex
        {
            public string Name;
            public bool   Nil;

            public HashSet<string> S;

            public Vertex(string name)
            {
                this.Name = name;
                Nil       = false;
                S         = new HashSet<string>();
            }
        }

        internal class Graph
        {
            public List<Vertex> Vertices = new List<Vertex>();

            public bool[,] Edges;

            public void InitEdges(int n)
            {
                Edges = new bool[n, n];
            }

            public Vertex FindVertex(string name)
            {
                foreach (var vertex in Vertices)
                    if (vertex.Name == name)
                        return vertex;
                return null;
            }

            public void AddVertex(Vertex v)
            {
                Vertices.Add(v);
            }

            public void Show()
            {
                foreach (var vertex in Vertices)
                {
                    Console.Write("name:" + vertex.Name + "  " + "null:" + vertex.Nil + "  " + "s:");
                    foreach (string s in vertex.S)
                    {
                        Console.Write(s + "  ");
                    }

                    Console.WriteLine();
                }

                Console.WriteLine();

                for (int i = 0; i < Edges.GetLength(0); i++)
                {
                    for (int j = 0; j < Edges.GetLength(1); j++)
                    {
                        Console.Write(Edges[i, j]);
                        Console.Write(" ");
                    }

                    Console.WriteLine();
                }
            }
        }

        internal class Production
        {
            public string Left;
            public string Right;

            public Production(string left, string right)
            {
                Left  = left;
                Right = right;
            }

            public override string ToString()
            {
                return Left + "->" + Right;
            }
        }

        public string Src   { get; set; }
        public string Input { get; set; }

        public List<string> Vn = new List<string>();
        public List<string> Vt = new List<string>();
        public List<string> V;

        private Graph _graph = new Graph();

        List<string> _list = new List<string>();

        private bool _change = false;

        public HashSet<string>[] First;
        public HashSet<string>[] Follow;

        private List<Production> _productions;

        public Production[][] Table;

        private List<LexicalAnalyzer.KeyValue> _lexicalResult;

        void Init()
        {
//            var lexicalAnalyzer =new LexicalAnalyzer(Input);
//            lexicalAnalyzer.Scanner();
//            _lexicalResult = lexicalAnalyzer.result;
            
            Vn.Add(Src[0].ToString());

            var regex   = new Regex(@"( [A-Z]'?\-)+");
            var matches = regex.Matches(Src);
            foreach (Match match in matches)
            {
                var s = match.Value;
                Vn.Add(new string(s.ToCharArray(1, s.Length - 2)));
            }

            _graph.InitEdges(Vn.Count);

            regex   = new Regex(@"[^A-Z' \-0|]");
            matches = regex.Matches(Src);
            foreach (Match match in matches)
            {
                Vt.Add(match.Value);
            }

            Vt.Add("$");

            First = new HashSet<string>[Vn.Count];

            Follow = new HashSet<string>[Vn.Count];
            for (var i = 0; i < Follow.Length; i++)
            {
                Follow[i] = new HashSet<string>();
            }

            Follow[0].Add("$");

            _productions = new List<Production>();

            Table = new Production[Vn.Count][];
            for (var i = 0; i < Table.Length; i++)
            {
                Table[i] = new Production[Vt.Count];
            }

            Productions = new List<string>[Vn.Count];
        }

        void CalGraph()
        {
            Regex vnRule = new Regex("^[A-Z]'?");

            var productions = Src.Split(" ");

            for (var i = 0; i < productions.Length; i++)
            {
                var production = productions[i];
                var leftright  = production.Split("-");
                var left       = leftright[0];
                var rights     = leftright[1].Split("|");

                Productions[i] = new List<string>();

                Vertex v = new Vertex(left);

                foreach (var right in rights)
                {
                    _productions.Add(new Production(left, right));
                    Productions[i].Add(right);

                    var start = vnRule.Match(right).Value;

                    if (start != "")
                    {
                        MakeArc(left, right, start);
                    }
                    else if (right == "0")
                    {
                        v.Nil = true;
                    }
                    else
                    {
                        v.S.Add(right[0].ToString());
                    }
                }

                _graph.AddVertex(v);
            }

            list:
            for (int i = 0; i < _list.Count; i += 2)
            {
                var left  = _list[i];
                var right = _list[i + 1];

                var start = vnRule.Match(right).Value;

                var a = _graph.FindVertex(left);

                if (_graph.FindVertex(start).Nil)
                {
                    if (start.Length == right.Length)
                    {
                        _list.RemoveAt(i);
                        _list.RemoveAt(i);
                        i -= 2;

                        if (!a.Nil)
                        {
                            a.Nil   = true;
                            _change = true;
                        }
                    }
                    else if (Vt.Contains(right[start.Length].ToString()))
                    {
                        string str = right[start.Length].ToString();
                        if (!a.S.Contains(str))
                        {
                            a.S.Add(str);
                        }

                        _list.RemoveAt(i);
                        _list.RemoveAt(i);
                        i -= 2;
                    }
                    else
                    {
                        _list.RemoveAt(i);
                        _list.RemoveAt(i);
                        i -= 2;

                        right = new string(right.ToCharArray(start.Length, right.Length - 1));
                        start = vnRule.Match(right).Value;

                        MakeArc(left, right, start);
                    }
                }
            }

            if (_change)
            {
                _change = false;
                goto list;
            }
        }

        void GetFirst()
        {
            var v = _graph.Vertices;

            bool[] visited = new bool[v.Count];

            for (int i = 0; i < _graph.Edges.GetLength(0); i++)
            {
                First[i] = new HashSet<string>();

                if (v[i].Nil)
                {
                    First[i].Add("0");
                }

                Dfs(First[i], i, visited);

                visited = new bool[v.Count];
            }
        }

        void GetFollow()
        {
            List<int>[] follow = new List<int>[Vn.Count];

            for (var i = 0; i < follow.Length; i++)
            {
                follow[i] = new List<int>();
            }

            Regex regex = new Regex(@"[A-Z][^\s|\|]*");

            var productions = Src.Split(" ");

            for (var i = 0; i < productions.Length; i++)
            {
                //一条生成式
                var leftright = productions[i].Split("-");

                var matches = regex.Matches(leftright[1]);

                if (matches.Count == 0)
                    continue;

                foreach (Match right in matches)
                {
                    //一条生成式的右半部分
                    var str   = right.Value;
                    var chars = Util.GetChars(str, 0);

                    for (int j = 0; j < chars.Count; j++)
                    {
                        //右半部分的一项
                        int index = Vn.IndexOf(chars[j]);

                        if (index == -1)
                        {
                            continue;
                        }

                        int k = j + 1;
                        for (; k < chars.Count; k++)
                        {
                            //去掉首字符的一项
                            var c  = chars[k];
                            var ci = Vn.IndexOf(c);

                            if (ci == -1)
                            {
                                Follow[index].Add(c);
                                break;
                            }

                            Follow[index].AppendWithoutNull(First[ci]);

                            if (!_graph.FindVertex(c).Nil)
                            {
                                break;
                            }
                        }

                        if (k == chars.Count && index != i)
                        {
                            follow[index].Add(i);
                        }
                    }
                }
            }

            loop:
            bool end = true;

            for (int i = 0; i < follow.Length; i++)
            {
                if (follow[i].Count != 0)
                {
                    for (var j = 0; j < follow[i].Count; j++)
                    {
                        int i1 = follow[i][j];

                        if (follow[i1].Count == 0)
                        {
                            Follow[i].Append(Follow[i1]);
                            follow[i].RemoveAt(j--);
                        }
                        else
                        {
                            end = false;
                        }
                    }
                }
            }

            if (!end)
                goto loop;
        }

        void GetTable()
        {
            foreach (var production in _productions)
            {
                var vni = Vn.IndexOf(production.Left);

                var c = Util.GetChar(production.Right, 0);

                if (Vn.Contains(c))
                {
                    foreach (string s in First[Vn.IndexOf(c)])
                    {
                        var vti = Vt.IndexOf(s);

                        Table[vni][vti] = production;
                    }
                }
                else if (c == "0")
                {
                    foreach (string s in Follow[vni])
                    {
                        var vti = Vt.IndexOf(s);

                        Table[vni][vti] = production;
                    }
                }
                else
                {
                    var vti = Vt.IndexOf(c);

                    Table[vni][vti] = production;
                }
            }

            for (int i = 0; i < Vn.Count; i++)
            {
                foreach (var c in Follow[i])
                {
                    var vti = Vt.IndexOf(c);

                    if (Table[i][vti] == null)
                    {
                        Table[i][vti] = new Production("synch", "");
                    }
                }
            }
        }

        void Up2DownParse()
        {
            Stack<string> stack = new Stack<string>();
            stack.Push("$");
            var x = Vn[0];
            stack.Push(x);

            for (int ip = 0; x != "$"; x = stack.Peek())
            {
                var stackOutput = new StringBuilder();

                foreach (string s in stack)
                {
                    stackOutput.Append(s);
                }

                Console.Write("{0,10}", stackOutput);

                var input = new StringBuilder();
                for (var i = ip; i < Input.Length; i++)
                {
                    input.Append(Input[i]);
                }

                Console.Write("{0,10}", input);

                if (x == Input[ip].ToString())
                {
                    stack.Pop();
                    Console.WriteLine("   匹配" + Input[ip]);
                    ip++;
                    continue;
                }

                int vni = Vn.IndexOf(x);
                int vti = Vt.IndexOf(Input[ip].ToString());

                var tableItem = Table[vni][vti];

                if (tableItem == null)
                {
                    Console.WriteLine("   错误：忽略" + Input[ip]);
                    ip++;
                }
                else if (tableItem.Left == "synch")
                {
                    Console.WriteLine("   错误：弹出" + stack.Peek());
                    stack.Pop();
                }
                else if (tableItem.Left == x)
                {
                    Console.WriteLine("   输出" + tableItem.ToString());

                    stack.Pop();

                    if (tableItem.Right == "0")
                        continue;

                    var strs = Util.GetChars(tableItem.Right, 0);

                    for (int i = strs.Count - 1; i >= 0; i--)
                    {
                        stack.Push(strs[i]);
                    }
                }
            }
        }

        private List<string>[] Productions;

        internal class LR1Production
        {
            public string          Left    { get; }
            public string          Right   { get; }
            public int             Current { get; }
            public HashSet<string> Forward { get; set; }

            public LR1Production(string left, string right, HashSet<string> forward, int current = 0)
            {
                Left    = left;
                Right   = right;
                Current = current;
                Forward = forward;
            }

            public override bool Equals(object obj)
            {
                if (!(obj is LR1Production p))
                    return false;
                if (Current != p.Current)
                    return false;
                if (Left != p.Left)
                    return false;
                if (Right != p.Right)
                    return false;
                return p.Forward.Equal(Forward);
            }

            public bool EqualsWithoutForward(LR1Production p)
            {
                if (Left != p.Left)
                    return false;
                if (Right != p.Right)
                    return false;
                if (Current != p.Current) 
                    return false;
                return !Forward.Equal(p.Forward);
            }
        }

        private ActionItem[][] Action;
        private int[][]        Goto;
        private List<Item>     items;

        internal class ActionItem
        {
            public int Type;
            public int Shift;
            public string ReduceLeft;
            public string ReduceRight;

            public ActionItem(int type=0,int shift=-1,string reduceLeft="",string reduceRight="")
            {
                Type = type;
                Shift = shift;
                ReduceLeft = reduceLeft;
                ReduceRight = reduceRight;
            }

            public override bool Equals(object obj)
            {
                if (!(obj is ActionItem a))
                    return false;
                if (Type != a.Type)
                    return false;
                if (Shift != a.Shift)
                    return false;
                if (ReduceLeft != a.ReduceLeft)
                    return false;
                return ReduceRight == a.ReduceRight;
            }
        }

        HashSet<string> FIRST(LR1Production production)
        {
            var result = new HashSet<string>();

            var chars   = Util.GetChars(production.Right, production.Current);
            var forward = production.Forward;
            
            if (chars.Count == 1)
            {
                //说明·后面只有一个符号
                result.Append(forward);
            }
            else
            {
                var vn      = chars[1];
                var vnIndex = Vn.IndexOf(vn);
                if (vnIndex == -1)
                {
                    //说明是终结符
                    result.Add(vn);
                }
                else
                {
                    var first = First[vnIndex];
                    foreach (string s in first)
                    {
                        if (s == "0")
                        {
                            result.Append(forward);
                        }
                        else
                        {
                            result.Add(s);
                        }
                    }
                }
            }

            return result;
        }

        Item Simply(Item items)
        {
            items.Reset();
            items.TryMove();

            var added = new bool[items.Count()];
            
            var result = new Item();
            result.Add(items.Current());
            added[0] = true;

            while (result.TryMove())
            {
                var current = result.Current();
                
                items.Reset();
                var oneAdded = false;
                while (items.TryMove())
                {
                    if(added[items.CurrentNum()])
                        continue;
                    var item = items.Current();
                    if (current.EqualsWithoutForward(item))
                    {
                        current.Forward.Append(item.Forward);
                        added[items.CurrentNum()] = true;
                    }
                    else if(!oneAdded)
                    {
                        result.Add(item);
                        added[items.CurrentNum()] = true;
                        oneAdded = true;
                    }
                }
            }
            
            items.Reset();
            return result;
        }

        Item GOTO(Item item, string start)
        {
            var result = new Item();
            var itemNum = items.IndexOf(item);

            item.Reset();
            while (item.TryMove())
            {
                var current = item.Current();
                if (current.Right.Length == current.Current)
                {
                    //已经是结束项了，GOTO为空
                    if (current.Left == "Ex")
                    {
                        //接受
                        
                        Action[itemNum][Vt.Count-1]=new ActionItem(3);
                    }
                    else
                    {
                        //规约
                        foreach (string forward in current.Forward)
                        {
                            var x = items.IndexOf(item);
                            var y = Vt.IndexOf(forward);
                            var willAdd = new ActionItem(2, -1, current.Left, current.Right);
                            if (Action[x][y] != null && !Action[x][y].Equals(willAdd))
                            {
                                var type = Action[x][y].Type;
                                if (type == 1)
                                {
                                    throw new Exception("移入规约冲突");
                                }

                                if (type == 2)
                                {
                                    throw new Exception("规约规约冲突");
                                }
                            }
                            Action[x][y]=willAdd;
                        }
                    }

                    continue;
                }
                var v = Util.GetChar(current.Right, current.Current);
                if (v == start)
                {
                    //·后的符号就是GOTO的第二个参数（即检测到的符号），此时生成下一项（下一项存放在result中）
                    result.Add(new LR1Production(current.Left, current.Right, current.Forward, current.Current + 1));
                }
            }

            if (result.Count() == 0)
            {
                return null;
            }

            result = Closure(result);
            
            item.Reset();
            
            return result;
        }

        void Items()
        {
            items=new List<Item>();
            var i0 = new Item();
            i0.Add(new LR1Production("Ex", Vn[0], new HashSet<string> {"$"}));
            i0 = Closure(i0);
            items.Add(i0);
            //ite用于迭代，items用于存储，两者内容上是一样的
            var ite = new LinkList<Item>();
            ite.Add(i0);

            var iteNum = 0;//迭代的次数
            
            while (ite.TryMove())
            {
                var iteItem = ite.Current();
                PrintIteItem(iteNum++,iteItem);
                
                foreach (string v in V)
                {
                    var vti = Vt.IndexOf(v);
                    var destination = GOTO(iteItem, v);
                    if (destination != null)
                    {
                        //选择items而不是ite进行迭代，ite用于在迭代中增加项的情况，items用于不改变items的情况下
                        foreach (Item item in items)
                        {
                            if (item.First().Equals(destination.First()))
                            {
                                goto equal;
                            }
                        }

                        ite.Add(destination);
                        items.Add(destination);

                        Console.WriteLine("添加I{0}:",items.IndexOf(destination));
                        PrintItem(destination);
                        
                        equal: //destination在items中则进行下一次循环
                        if (vti != -1)
                        {
                            //移入
                            Action[items.IndexOf(iteItem)][vti] = new ActionItem(1,items.IndexOf(destination));
                        }
                        else
                        {
                            var vni = Vn.IndexOf(v);
                            var x = items.IndexOf(iteItem);
                            var y = items.IndexOf(destination);
                            Goto[x][vni] = y;
                        }
                    }
                }
            }
        }

        void PrintIteItem(int iteNum,Item item)
        {
            Console.WriteLine("正在处理I{0}:",iteNum);
            PrintItem(item);
            //Thread.Sleep(1000);
        }

        void PrintItem(Item item)
        {
            item.Reset();
            while (item.TryMove())
            {
                var current = item.Current();
                Console.Write(current.Left +"->");
                var right = current.Right;
                for (int i = 0; i < right.Length; i++)
                {
                    if (i == current.Current)
                    {
                        Console.Write("·");
                    }
                    Console.Write(right[i]);
                }

                if (right.Length == current.Current)
                {
                    Console.Write("·");
                }
                
                Console.Write(" , ");
                foreach (string s in current.Forward) 
                {
                    Console.Write(s +" ");
                }
                Console.WriteLine();
            }
            Console.WriteLine();
            item.Reset();
            
            //Thread.Sleep(1000);
        }

        void LR1Drive()
        {
            var state=new Stack<int>();
            var symbol=new Stack<string>();
            var symbolProperty=new Stack<(string,int)>();
            
            state.Push(0);

            for (var index = 0; index < Input.Length; )
            {
                char c        = Input[index];
                var  s        = c.ToString();
                var  vti      = Vt.IndexOf(s);
                var  stateTop = state.Peek();

                var lex = _lexicalResult[index];

                if (vti == -1)
                {
                    Console.WriteLine("无此符号");
                    return;
                }
                
                var action = Action[stateTop][vti];

                if (action == null || action.Type == 0)
                {
                    Console.WriteLine("error");
                    return;
                }
                if (action.Type == 1)
                {
                    state.Push(action.Shift);
                    symbol.Push(s);
                    //symbolProperty.Push(lex.Value == "Digit" ? ("Digit", int.Parse(lex.Key)) : ("Symbol", -1));
                    index++;
                }
                else if (action.Type == 2)
                {
                    var popNum = Util.GetChars(action.ReduceRight).Count;
                    for (int i = 0; i < popNum; i++)
                    {
                        symbol.Pop();
                        //symbolProperty.Pop();
                        state.Pop();
                    }

                    stateTop = state.Peek();
                    var gotoi = Goto[stateTop][Vn.IndexOf(action.ReduceLeft)];

                    symbol.Push(action.ReduceLeft);
                    state.Push(gotoi);
                }
                else
                {
                    Console.WriteLine("Accept");
                    return;
                }
            }
        }

        void LR1()
        {
            V = Vn.Merge(Vt);
            
            //规定0为无动作，1为移入，2为规约，3为接受
            int itemNum =  100;

            Action = new ActionItem[itemNum][];
            for (int i = 0; i < Action.Length; i++)
            {
                Action[i] = new ActionItem[Vt.Count];
            }

            Goto = new int[itemNum][];
            for (int i = 0; i < Goto.Length; i++)
            {
                Goto[i] = new int[Vn.Count];
            }
            
            Items();
            //PrintItems();
            
            //LR1Drive();
        }

        void PrintActionGoto()
        {
            for (int i = 0; i < items.Count; i++)
            {
                for (int j = 0; j < Vt.Count; j++)
                {
                    var a = Action[i][j];
                    if (a == null)
                    {
                        Console.Write("error ");
                    }
                    else
                    {
                        if (a.Type == 1)
                        {
                            Console.Write("s");
                            Console.Write(a.Shift);
                            Console.Write(" ");
                        }
                        else if(a.Type ==2)
                        {
                            Console.Write("r");
                            Console.Write(a.ReduceLeft  + "->");
                            Console.Write(a.ReduceRight + " ");
                        }
                        else if (a.Type == 3)
                        {
                            Console.Write("acc");
                        }
                    }
                }

                for (int j = 0; j < Vn.Count;j++)
                {
                    var g = Goto[i][j];
                    Console.Write(g);
                    Console.Write(" ");
                }
                Console.WriteLine();
            }
        }

        public void PrintItems()
        {
            int num = 0;
            foreach (Item item in items)
            {
                Console.WriteLine("I{0}:",num++);
                item.Reset();
                while (item.TryMove())
                {
                    var current = item.Current();
                    Console.Write(current.Left +"->");
                    var right = current.Right;
                    for (int i = 0; i < right.Length; i++)
                    {
                        if (i == current.Current)
                        {
                            Console.Write("·");
                        }
                        Console.Write(right[i]);
                    }

                    if (right.Length == current.Current)
                    {
                        Console.Write("·");
                    }
                
                    Console.Write(" , ");
                    foreach (string s in current.Forward) 
                    {
                        Console.Write(s +" ");
                    }
                    Console.WriteLine();
                }
                item.Reset();
                Console.WriteLine();
            }
        }

        void Dfs(HashSet<string> firstSet, int i, bool[] visited)
        {
            firstSet.Append(_graph.Vertices[i].S);

            visited[i] = true;

            for (int j = 0; j < _graph.Vertices.Count; j++)
            {
                if (_graph.Edges[i, j] && !visited[j])
                {
                    Dfs(firstSet, j, visited);
                }
            }
        }

        void MakeArc(string left, string right, string start)
        {
            _list.Add(left);
            _list.Add(right);

            if (left != start && !_graph.Edges[Vn.IndexOf(left), Vn.IndexOf(start)])
            {
                _graph.Edges[Vn.IndexOf(left), Vn.IndexOf(start)] = true;
            }
        }

        public Item Closure(Item items)
        {
            while (items.TryMove())
            {
                var item = items.Current();
                if (item.Right.Length == item.Current)
                {
                    //说明·后没有字符
                    continue;
                }

                var v  = Util.GetChar(item.Right, item.Current);
                var vi = Vn.IndexOf(v);
                if (vi == -1)
                {
                    //说明·后第一个字符是终结符
                    continue;
                }

                var rights     = Productions[vi];
                var endSymbols = FIRST(item);
                foreach (string right in rights)
                {
                    var willAdd = new LR1Production(v, right, endSymbols);

                    if (items.IteForEqual(willAdd) != null)
                        continue;

                    items.Add(willAdd);
                }
            }
            
            items = Simply(items);
            //PrintItem(items);
            return items;
        }
        
        public void Excute()
        {
            Init();

            CalGraph();

            GetFirst();

            GetFollow();

            GetTable();

            Up2DownParse();
            
            //LR1();
                
            //LR1Drive();
        }

        public void Print()
        {
            _graph.Show();

            Console.WriteLine();

            for (var i = 0; i < First.Length; i++)
            {
                Console.Write("FIRST(" + Vn[i] + ") : {");

                foreach (string s1 in First[i])
                {
                    Console.Write(s1 + ",");
                }

                Console.WriteLine("}");
            }

            Console.WriteLine();

            for (var i = 0; i < Follow.Length; i++)
            {
                Console.Write("Follow(" + Vn[i] + ") : {");

                foreach (string s1 in Follow[i])
                {
                    Console.Write(s1 + ",");
                }

                Console.WriteLine("}");
            }

            Console.WriteLine();

            for (int i = 0; i < Vt.Count; i++)
            {
                Console.Write("   " + Vt[i] + "  ");
            }

            Console.WriteLine();
            for (int i = 0; i < Table.Length; i++)
            {
                Console.Write(Vn[i] + " ");
                for (int j = 0; j < Table[i].Length; j++)
                {
                    if (Table[i][j] == null)
                    {
                        Console.Write("null  ");
                    }
                    else
                    {
                        Console.Write(Table[i][j].Left + "-" + Table[i][j].Right + "  ");
                    }
                }

                Console.WriteLine();
            }
            
            PrintActionGoto();
            PrintItems();
        }
    }
}