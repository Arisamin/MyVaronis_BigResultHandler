
using System;
using System.Collections.Generic;
using System.Linq;

namespace Playground
{
    class Program
    {
        static void Main(string[] args)
        {
            RunRotateArray();
            //LowestCommonAncestorCase();
            //CheckBInaryTreeBalance();
            //Fib(5);
            //RunFlattenList();
        }

        static void RunRotateArray()
        {
            object[] arr = new object[] { 1, 2, 3, 4, 5 };

            Console.WriteLine("Original array:");
            PrintArray(arr);

            int steps = 2;
            var rotatedArr = Rotate(arr, steps);

            Console.WriteLine($"Array after rotating {steps} steps:");
            PrintArray(rotatedArr);
        }

        static object[] Rotate(object[] a, int s)
        {
            for(int i=1 ; i<=s ; i++)
                a = RotateOnce(a);
            
            return a;
        }

        static object[] RotateOnce(object[] a)
        {
            object spare = a[a.Length-1];
            
            for(int i=a.Length-1 ; i>0 ; i--)
                a[i] = a[i-1];

            a[0] = spare;
            
            return a;
        }

        // Prints the values of an array, separated by spaces
        static void PrintArray<T>(T[] arr)
        {
            if (arr == null)
            {
                Console.WriteLine("(null array)");
                return;
            }
            Console.WriteLine(string.Join(" ", arr));
        }

        static void LowestCommonAncestorCase()
        {
            // Example tree:
            //     			  1
            //     		  /  	   \
            //     		2			 3
            //     	  /  \			/ \
            //     	4	   5	   6   7
            //    /  \      \	  /     \
            //   7	  8      9   10     11
            var root = new TreeNode<int>(1,
                new TreeNode<int>(2,
                    new TreeNode<int>(4,
                        new TreeNode<int>(7),
                        new TreeNode<int>(8)
                    ),
                    new TreeNode<int>(5,
                        null,
                        new TreeNode<int>(9)
                    )
                ),
                new TreeNode<int>(3,
                    new TreeNode<int>(6,
                        new TreeNode<int>(10),
                        null
                    ),
                    new TreeNode<int>(11)
                )
            );

            // var node8 = new TreeNode<int>(12); // not in tree
            var node8 = root.Left.Left.Right; // 8
            var node9 = root.Left.Right.Right; // 9

            var lca = LowestCommonAncestor(root, node8, node9);

            if(lca == null)
            {
                Console.WriteLine("Lowest Common Ancestor not found");
                return;
            }

            Console.WriteLine($"Lowest Common Ancestor of {node8.Value} and {node9.Value} is: {lca.Value}");
        } 

        static TreeNode<T> LowestCommonAncestor<T>(TreeNode<T> r, TreeNode<T> a, TreeNode<T> b)
        {
            var nodePathA = FindPath(r, a, new TreeNode<T>[0]);
            var nodePathB = FindPath(r, b, new TreeNode<T>[0]);

            if(nodePathA == null || nodePathB == null)
                return null;
            
            TreeNode<T> commonAnc = null;
            
            for(int i = 0 ; i<nodePathA.Length && i<nodePathB.Length ; i++)
                if(nodePathA[i] == nodePathB[i])
                    commonAnc = nodePathA[i];
                    
            return commonAnc;
        }

        static TreeNode<T>[] FindPath<T>(TreeNode<T> r, TreeNode<T> x, TreeNode<T>[] accPath)
        {
            accPath = AddToTail(accPath, r);
            
            if(ReferenceEquals(r, x))
                return accPath;
                
            TreeNode<T>[] leftPath = null;
            TreeNode<T>[] rightPath = null;
                
           	if(r.Left != null)
            {
                leftPath = FindPath(r.Left, x, accPath);
                if(leftPath != null)
                    return leftPath;
            }
                
            if(r.Right != null)
            {
                rightPath = FindPath(r.Right, x, accPath);
                if(rightPath != null)
                    return rightPath;
            }
            
            return null;
        }

        public static T[] AddToTail<T>(T[] array, T item)
        {
            if (array == null)
                return new T[] { item };

            T[] result = new T[array.Length + 1];
            Array.Copy(array, result, array.Length);
            result[array.Length] = item;
            return result;
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
