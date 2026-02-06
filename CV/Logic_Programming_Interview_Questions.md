# Classic Logic & Programming Interview Questions

This document collects classic interview questions that test logical thinking and programming skills. Each question is listed with space for your answer and notes.

---

---

## Combined Questions, Requirements, and Answers

### 1. Reverse a linked list
**Time:** O(n) &nbsp;&nbsp; **Space:** O(1)

    ListNode ReverseList(ListNode head)
    {
        return ReverseNext(head, null);
    }
    ListNode ReverseNext(ListNode original, ListNode rev)
    {
        if(original == null) return rev;
        origOldHead = original;
        origNewHead = original.Next;
        origOldHead.Next = rev;
        return ReverseNext(origNewHead, origOldHead);
    }

---

### 2. First non-repeating character in a string
**Time:** O(n) &nbsp;&nbsp; **Space:** O(1) for fixed alphabet, O(n) for general

    char FirstNonRepeating(string s)
    {
        charDict = new Dict<char,int>();
        for(int i=0 ; i<s.Length; i++)
        {
            c = s.charAt(i);
            if(!charDict.contains(c))
                charDict.Add(c,1);
            else
                charDict.update(c,charDict[c]+1);
        }
        repeatingChars = charDict.Where( kv => kv.value > 1).Select(kv => kv.Key);
        foreach(key in repeatingChars)
            charDict.Remove(key);
        for(int i=0 ; i<s.Length; i++)
        {
            c = s.charAt(i);
            if(charDict.contains(c))
                return c;
        }
        return null;
    }

---

### 3. Detect a cycle in a linked list
**Time:** O(n) &nbsp;&nbsp; **Space:** O(1) (Floyd's), O(n) (hash set)

    bool FoundLoop(ListNode n)
    {
        return FoundLoop(n, n.Next);
    }
    bool FoundLoop(ListNode list, ListNode fastList)
    {
        if(fastList == null) return false;
        if(ReferenceEquals(list, fastList)) return true;
        newFastList = fastList.Next == null ? null : fastList.Next.Next;
        return FoundLoop(list.Next, newFastList);
    }

---

### 4. Check if a string is a palindrome
**Time:** O(n) &nbsp;&nbsp; **Space:** O(1)

    bool IsPalindrome(string s)
    {
        if(s.Length%2 == 0)
            return IsPalindrome(s.Length/2-1, s.Length/2);
        return IsPalindrome(s.Length/2-1.5,s.Length/2+0.5, s);
    }
    bool IsPalindrome(int lowIndex, int highIndex, string s)
    {
        if(lowIndex < 0) return true;
        if(s[lowIndex] != s[highIndex]) return false;
        return IsPalindrome(lowIndex-1, highIndex+1);
    }

---

### 5. Find the missing number in 1..n
**Time:** O(n) &nbsp;&nbsp; **Space:** O(1)

    int FindMissing(int[] ar)
    {
        int expectedSum = 0;
        int sum = 0;
        for(int i=0 ; i<ar.length ; i++)
            sum += ar[i];
        for(int i=1 ; i<=ar.length+1 ; i++)
            expectedSum += i;
        return expectedSum - sum;
    }

---

### 6. Merge two sorted arrays
**Time:** O(n + m) &nbsp;&nbsp; **Space:** O(n + m)

    int[] Merge(int[] r1, int[] r2)
    {
        if(r1.Length == 0) return r2;
        if(r2.Length == 0) return r1;
        int i1 = 0, i2 = 0;
        int[] mr = new int[r1.Length + r2.Length];
        for(int mi=0 ; mi<mr.Length ; mi++)
        {
            if(i1 >= r1.Length) { mr[mi] = r2[i2++]; continue; }
            if(i2 >= r2.Length) { mr[mi] = r1[i1++]; continue; }
            if(r1[i1] < r2[i2]) mr[mi] = r1[i1++];
            else mr[mi] = r2[i2++];
        }
        return mr;
    }

---

### 7. Check if two strings are anagrams
**Time:** O(n) &nbsp;&nbsp; **Space:** O(1) for fixed alphabet, O(n) for general

    bool IsAnagram(string s1, string s2)
    {
        if(s1.Length != s2.Length) return false;
        charMap1 = new Dict<char,int>();
        charMap2 = new Dict<char,int>();
        for(int i=0 ; i<s1.Length ; i++)
        {
            charMap1[i] = s1[i];
            charMap2[i] = s2[i];
        }
        if(charMap1.Count != charMap2.Count) return false;
        for(int i=0 ; i<charMap1.Count ; i++)
        {
            ch = char1Map[i];
            if(!charMap2.Contains(ch) || char1Map[ch] != char2Map[ch]) return false;
        }
        return true;
    }

---

### 8. Intersection point of two linked lists
**Time:** O(n + m) &nbsp;&nbsp; **Space:** O(1)

    ListNode GetIntersectionPoint(ListNode a, ListNode b)
    {
        revA = a.ReverseList();
        revB = b.ReverseList();
        ListNode xPoint = null;
        do {
            if(!RefEquals(revA, revB)) return xPoint;
            xPoint = revA;
            revA = revA.Next;
            revB = revB.Next;
        } while(revA.HasNext() && revB.HasNext());
        return xPoint;
    }

---

### 9. Flatten a nested list/array
**Time:** O(n) &nbsp;&nbsp; **Space:** O(n)

    static List<object> Flatten(List<object> a)
    {
        return Flatten(a, new List<object>());
    }
    static List<object> Flatten(object a, List<object> flat)
    {
        if(a is null) return flat;
        if(a is not List<object>) { flat.Add(a); return flat; }
        var ax = a as List<object>;
        if(ax.Count == 1) return Flatten(ax[0], flat);
        if(ax.Count > 1) return Flatten(ax.Skip(1).ToList(), Flatten(ax[0], flat));
        return flat;
    }

---

### 10. Find the nth Fibonacci number (no loops)
**Time:** O(n) (linear recursion or DP), O(1) (Binet's) &nbsp;&nbsp; **Space:** O(n) (recursion), O(1) (iterative/closed)

    static int Fib(int n)
    {
        if(n == 0) return 0;
        if(n <= 2) return 1;
        var f1 = Fib(n-1);
        var f2 = Fib(n-2);
        return f1 + f2;
    }

---

### 11. Check if a binary tree is balanced
**Time:** O(n) &nbsp;&nbsp; **Space:** O(h)

    static bool IsBalancedTree(TreeNode<int> r)
    {
        var left = CountNodes(r.Left, 0);
        var right = CountNodes(r.Right, 0);
        return Math.Abs(left - right) <= 1;
    }
    static int CountNodes(TreeNode<int> t, int count)
    {
        if (t == null) return count;
        count++;
        var withLeft = CountNodes(t.Left, count);
        var withRight = CountNodes(t.Right, withLeft);
        return withRight;
    }

---

### 12. Lowest common ancestor in a binary tree
**Time:** O(n) &nbsp;&nbsp; **Space:** O(h)

    static TreeNode<T> LowestCommonAncestor<T>(TreeNode<T> r, TreeNode<T> a, TreeNode<T> b)
    {
        var nodePathA = FindPath(r, a, new TreeNode<T>[0]);
        var nodePathB = FindPath(r, b, new TreeNode<T>[0]);
        if(nodePathA == null || nodePathB == null) return null;
        TreeNode<T> commonAnc = null;
        for(int i = 0 ; i<nodePathA.Length && i<nodePathB.Length ; i++)
            if(nodePathA[i] == nodePathB[i])
                commonAnc = nodePathA[i];
        return commonAnc;
    }
    static TreeNode<T>[] FindPath<T>(TreeNode<T> r, TreeNode<T> x, TreeNode<T>[] accPath)
    {
        accPath = AddToTail(accPath, r);
        if(ReferenceEquals(r, x)) return accPath;
        TreeNode<T>[] leftPath = null;
        TreeNode<T>[] rightPath = null;
        if(r.Left != null)
        {
            leftPath = FindPath(r.Left, x, accPath);
            if(leftPath != null) return leftPath;
        }
        if(r.Right != null)
        {
            rightPath = FindPath(r.Right, x, accPath);
            if(rightPath != null) return rightPath;
        }
        return null;
    }

---

### 13. Rotate an array by k steps
**Time:** O(n) &nbsp;&nbsp; **Space:** O(1) (in-place), O(n) (extra array)

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

solution 2: reversing

object[] Rotate(object[] a, int k)
{
	// reverse everything
	// reverse first k
	// reverse last n-k
	
	int n = a.Length;
	
	a = Reverse(a, 0, n-1);
	a = Reverse(a, 0, k-1);
	a = Reverse(a, k, n-1);
	
	return a;
}


object[] Reverse(object[] a, int start, int end)
{
	int t;
	
	for(int i=0 ; start+i <= end-i ; i++)
	{
		t = a[start+i];
		a[start+i] = a[end-i];
		a[end-i] = t;
	}
	
	return a;
}
---

### 14. Remove duplicates from a sorted array
**Time:** O(n) &nbsp;&nbsp; **Space:** O(1)

// (Add your implementation here)

---

### 15. Longest substring without repeating characters
**Time:** O(n) &nbsp;&nbsp; **Space:** O(min(n, m)), m = alphabet size

// (Add your implementation here)

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
