
using System;
using System.Collections.Generic;
using System.Linq;

namespace Playground
{
    class Program
    {
        static void Main(string[] args)
        {
            CheckBInaryTreeBalance();
            //Fib(5);
            //RunFlattenList();
        }

        static void CheckBInaryTreeBalance()
        {

            // balanced
            //      1
            //    /   \
            //   2     3
            //  / \   / \
            // 4   5 6   7
            var root = new TreeNode<int>(1,
                new TreeNode<int>(2,
                    new TreeNode<int>(4),
                    new TreeNode<int>(5)
                ),
                new TreeNode<int>(3,
                    new TreeNode<int>(6),
                    new TreeNode<int>(7)
                )
            );

            // unbalanced
            //      1
            //    /   \
            //   2     3
            //  / \   
            // 4   5 
            // var root = 
            //     new TreeNode<int>(
            //         1,
            //         new TreeNode<int>(2,
            //             new TreeNode<int>(4),
            //             new TreeNode<int>(5)
            //         ),
            //         new TreeNode<int>(3)
            //     );

            Console.WriteLine(IsBalancedTree(root) ? "Balanced" : "Not Balanced");
        }

        static bool IsBalancedTree(TreeNode<int> r)
        {
            var left = CountNodes(r.Left, 0);
            var right = CountNodes(r.Right, 0);
            
            Console.WriteLine($"Left: {left}, Right: {right}");

            return Math.Abs(left - right) <= 1;
        }

        static int CountNodes(TreeNode<int> t, int count)
        {
            if (t == null)
                return count;

            count++;
            
            var withLeft = CountNodes(t.Left, count);
                
            var withRight = CountNodes(t.Right, withLeft);
            
            return withRight;
        }

        // Fib(5)
        //    1) Fib(4)
        //        1) Fib(3)
        //            1) Fib(2) = 1 // "1"
        //            2) Fib(1) = 1 // "1"
        //          => "2"
        //        2) Fib(2) = 1 // "1"
        //       => "3"
        //    2) Fib(3)
        //        1) Fib(2) = 1 // "1"
        //        1) Fib(1) = 1 // "1"
        //       => "2"
        //  => "5"
        static int Fib(int n)
        {
            if(n == 0)
            {
                Console.WriteLine("0");
                return 0;
            }
                
            if(n <= 2)
            {
                Console.WriteLine("1");
                return 1;
            }

            var f1 = Fib(n-1);
            var f2 = Fib(n-2);

            Console.WriteLine(f1 + f2);

            return f1 + f2;
        }

        static void RunFlattenList()
        {
            //var a = new List<object>(){1, new List<object>(){2,3}, 4};
            var a = new List<object>(){1, new List<object>(), 2};

            var flat = Flatten(a);

            foreach(var item in flat)
                Console.WriteLine(item);
        }

        static List<object> Flatten(List<object> a)
        {
            return Flatten(a, new List<object>());
        }

        // [1, [2,3], 4], []
        //      1) 1, [] = [1]
        //      2) [[2,3],4], [1]
        // 
        // [[2,3],4], [1]
        //      1) [2,3], [1] = [1,2,3] 
        //      2) [4], [1,2,3]
        //
        //  [2,3],[1]
        //      1) 2, [1] = [1,2]
        //      2) [3], [1,2] = [1,2,3]
        //
        //  [3], [1,2]
        //      3, [1,2] = [1,2,3]
        //
        //  [4], [1,2,3]
        //      4, [1,2,3] = [1,2,3,4]

        // [1, [], 2]
        //      1) 1, [] = [1]
        //      2) [[],2], [1]
        // [[],2], [1]
        //    1) [], [1] = [1]
        //    2) [2], [1]
        // [2], [1] = 2, [1] = [1,2]
        static List<object> Flatten(object a, List<object> flat)
        {	
            if(a is null)
                return flat;
                
            if(a is not List<object>)
            {
                flat.Add(a);
                return flat;
            }

            var ax = a as List<object>;

            if(ax.Count == 1)
            {
                return Flatten(ax[0], flat);
            }

            if(ax.Count >  1)
            {
                return Flatten(ax.Skip(1).ToList(), Flatten(ax[0], flat));
            }

            return flat;
        }
    }

    public class TreeNode<T>
    {
        public T Value;
        public TreeNode<T> Left;
        public TreeNode<T> Right;

        public TreeNode(T value, TreeNode<T> left = null, TreeNode<T> right = null)
        {
            Value = value;
            Left = left;
            Right = right;
        }
    }
}
