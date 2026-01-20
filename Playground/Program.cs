
using System;
using System.Collections.Generic;
using System.Linq;

namespace Playground
{
    class Program
    {
        static void Main(string[] args)
        {
            RunFlattenList();
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
}
