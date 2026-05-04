using ChatRagAppConfiguration;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using System.Security.Cryptography;
using System.Text;

namespace ChatRagApp.Services.Utils;
 public static class ImageUtils
 {
     private static readonly string imageCLIPLocalModel = "clip-image-vit-32-float32.onnx";
     private static readonly string textCLIPLocalModel = "clip-text-vit-32-float32-int32.onnx";

     public record ImageIndexEntry(string Path, string Description, float[] ImageEmbedding, float[] DescriptionEmbedding);

     // Configuration-backed settings
     private static readonly int ResizeSize;
     private static readonly int Grid;          

    static ImageUtils()
     {
         
         var cfg = ImageSearchConfigurationInfo.GetImageSearchConfigurationInfo();
         ResizeSize = Math.Max(1, cfg.ImageResize);
         Grid = Math.Max(1, cfg.SpatialGrid);
     }
     
     public static float[] ComputeImageEmbedding(string path)
     {            
         using var img = Image.Load<Rgba32>(path);
         img.Mutate(x => x.Resize(new ResizeOptions { Size = new Size(ResizeSize, ResizeSize), Mode = ResizeMode.Crop }));
         var emb = new List<float>(Grid * Grid * 4); //Each block contributes 4 floats(R, G, B, A normalized to 0..1 by dividing by 255)
         int blockW = Math.Max(1, img.Width / Grid);
         int blockH = Math.Max(1, img.Height / Grid);
         for (int by = 0; by < Grid; by++)
         {
             for (int bx = 0; bx < Grid; bx++)
             {
                 double r = 0, g = 0, b = 0, a = 0;
                 int count = 0;
                 for (int y = by * blockH; y < Math.Min((by + 1) * blockH, img.Height); y++)
                 {
                     for (int x = bx * blockW; x < Math.Min((bx + 1) * blockW, img.Width); x++)
                     {
                         var p = img[x, y];
                         r += p.R;
                         g += p.G;
                         b += p.B;
                         a += p.A;
                         count++;
                     }
                 }
                 if (count == 0) count = 1;
                 emb.Add((float)(r / count / 255.0));
                 emb.Add((float)(g / count / 255.0));
                 emb.Add((float)(b / count / 255.0));
                 emb.Add((float)(a / count / 255.0));
             }
         }

         var arr = emb.ToArray();
         // Normalize image embedding so cosine similarity becomes a simple dot product at query time
         NormalizeInPlace(arr);
         return arr; // length = grid * grid * 4
     }

     public static float[] GetCLIPImageEmbedding(string imagePath)
     {
         // In[ ]: Load model
         using var session = new InferenceSession(imageCLIPLocalModel);
         // In[ ]: Load/preprocess image (crop, resize 224x224, normalize RGB channels)
         using var image = Image.Load<Rgba32>(imagePath); // Process to tensor

         // In[ ]: Create input tensor (placeholder — fill with zeros or normalized pixels)
         var inputTensor = new DenseTensor<float>(new[] { 1, 3, 224, 224 }); // Fill with normalized pixels

         // In[ ]: Example: fill with zeros (replace with real preprocessing)
         for (int n = 0; n < 1; n++)
         {
             for (int c = 0; c < 3; c++)
             {
                 for (int h = 0; h < 224; h++)
                 {
                     for (int w = 0; w < 224; w++)
                     {
                         inputTensor[n, c, h, w] = 0f;
                     }
                 }
             }
         }

         var inputs = new List<NamedOnnxValue> { NamedOnnxValue.CreateFromTensor("input", inputTensor) };
         using var results = session.Run(inputs);
         float[] imgEmbedding = results.First().AsTensor<float>().ToArray(); // 512-dim vector

         // Normalize image embedding so cosine similarity becomes a simple dot product at query time
         NormalizeInPlace_CLIP(imgEmbedding);

         return imgEmbedding;
     }

     public static float[] ComputeTextEmbeddingApprox(string text)
     {
         var dims = Grid * Grid * 4;//Each block contributes 4 floats (R,G,B,A normalized to 0..1 by dividing by 255)
         var emb = new float[dims];
         if (string.IsNullOrWhiteSpace(text)) return emb;

         var words = System.Text.RegularExpressions.Regex.Matches(text.ToLowerInvariant(), "\\p{L}+")
             .Cast<System.Text.RegularExpressions.Match>()
             .Select(m => m.Value);

         foreach (var w in words)
         {
             var h = HashToInt(w);
             var idx = Math.Abs(h) % dims;
             emb[idx] += 1.0f;
         }
         var norm = Math.Sqrt(emb.Select(x => x * x).Sum());
         if (norm > 0)
         {
             for (int i = 0; i < emb.Length; i++) emb[i] /= (float)norm;
         }
         return emb;
     }

     public static float[] GetCLIPTextEmbedding(string textInput)
     {
         // Create session
         using var textSession = new InferenceSession(textCLIPLocalModel);

         // Inspect input metadata to build appropriate inputs
         var inputMeta = textSession.InputMetadata;

         // Determine a sequence length (default to 77 used by CLIP)
         int defaultSeq = 77;
         int seqLen = defaultSeq;
         foreach (var meta in inputMeta.Values)
         {
             var dims = meta.Dimensions;
             if (dims != null && dims.Length >= 2)
             {
                 // prefer a positive static dim if available
                 if (dims[1] > 0) seqLen = dims[1];
                 break;
             }
         }

         // Very simple tokenizer placeholder: encode UTF8 bytes into ints (not BPE)
         var bytes = Encoding.UTF8.GetBytes(textInput);
         var tokens = new int[seqLen];
         for (int i = 0; i < seqLen; i++) tokens[i] = 0;
         for (int i = 0; i < seqLen && i < bytes.Length; i++)
         {
             // map byte to a pseudo-vocab id in a stable way
             tokens[i] = (bytes[i] % 49407); // 49407 is a common CLIP vocab size candidate
         }

         // Build NamedOnnxValue list for all inputs the model expects
         var namedInputs = new List<NamedOnnxValue>();

         foreach (var kvp in inputMeta)
         {
             var name = kvp.Key;
             var meta = kvp.Value;
             var dims = meta.Dimensions ?? new int[] { 1, seqLen };

             // Replace dynamic or negative dims: assume first dim is batch=1, second is seqLen
             var shape = dims.Select((d, idx) => (d > 0) ? d : (idx == 0 ? 1 : seqLen)).ToArray();

             var elementType = meta.ElementType;

             if (elementType == typeof(int) || elementType == typeof(Int32))
             {
                 var tensor = new DenseTensor<int>(shape);
                 if (shape.Length == 2 && shape[0] == 1)
                 {
                     for (int i = 0; i < shape[1] && i < seqLen; i++) tensor[0, i] = tokens[i];
                 }
                 namedInputs.Add(NamedOnnxValue.CreateFromTensor<int>(name, tensor));
             }
             else if (elementType == typeof(long) || elementType == typeof(Int64))
             {
                 var tensor = new DenseTensor<long>(shape);
                 if (shape.Length == 2 && shape[0] == 1)
                 {
                     for (int i = 0; i < shape[1] && i < seqLen; i++) tensor[0, i] = tokens[i];
                 }
                 namedInputs.Add(NamedOnnxValue.CreateFromTensor<long>(name, tensor));
             }
             else
             {
                 // float or other numeric types -> fill with zeros
                 var tensor = new DenseTensor<float>(shape);
                 namedInputs.Add(NamedOnnxValue.CreateFromTensor<float>(name, tensor));
             }
         }

         using var results = textSession.Run(namedInputs);
         var outTensor = results.First().AsTensor<float>();
         return outTensor.ToArray();
     }

     private static void NormalizeInPlace(float[] v)
     {
         if (v == null) return;
         double sum = 0;
         for (int i = 0; i < v.Length; i++) sum += (double)v[i] * v[i];
         if (sum <= 0) return;
         var norm = Math.Sqrt(sum);
         for (int i = 0; i < v.Length; i++) v[i] = (float)(v[i] / norm);
     }

     public static int HashToInt(string s)
     {
         using var sha = SHA256.Create();
         var bytes = sha.ComputeHash(Encoding.UTF8.GetBytes(s));
         int val = BitConverter.ToInt32(bytes, 0);
         return val;
     }

     public static float CosineSimilarity(float[] a, float[] b)
     {
         if (a == null) throw new ArgumentNullException(nameof(a));
         if (b == null) throw new ArgumentNullException(nameof(b));
         if (a.Length != b.Length) throw new ArgumentException("Vectors must have the same length", nameof(b));

         double dot = 0; double na = 0; double nb = 0;
         for (int i = 0; i < a.Length; i++)
         {
             dot += (double)a[i] * b[i];
             na += (double)a[i] * a[i];
             nb += (double)b[i] * b[i];
         }

         // if either vector is zero-length return 0 (no similarity)
         if (na == 0 || nb == 0) return 0f;

         // if both vectors are already normalized (||v||^2 near 1) then cosine = dot
         const double eps = 1e-3;
         if (Math.Abs(na - 1.0) < eps && Math.Abs(nb - 1.0) < eps)
         {
             return (float)dot;
         }
        var cosSim = (float)(dot / (Math.Sqrt(na) * Math.Sqrt(nb)));
         return cosSim;
     }

     public static float CLIPCosineSimilarity(float[] a, float[] b)
     {
         if (a.Length != b.Length)
             throw new ArgumentException("a.Length != b.Length");

         float dotProduct = 0f;
         float mA = 0f;
         float mB = 0f;

         for (int i = 0; i < a.Length; i++)
         {
             dotProduct += a[i] * b[i];
             mA += a[i] * a[i];
             mB += b[i] * b[i];
         }

         mA = (float)Math.Sqrt(mA);
         mB = (float)Math.Sqrt(mB);

         float similarity = dotProduct / (mA * mB);
         return similarity;
     }

     public static float CLIPCosineSimilarity2(float[] a, float[] b)
     {
         float dot = 0f;
         for (int i = 0; i < a.Length; i++) dot += a[i] * b[i];
         return dot;
     }

     private static void NormalizeInPlace_CLIP(Span<float> vec)
     {
         float norm = 0f;
         for (int i = 0; i < vec.Length; i++) norm += vec[i] * vec[i];
         norm = MathF.Sqrt(norm);
         for (int i = 0; i < vec.Length; i++) vec[i] /= norm;
     }

     public static string[] SplitArgs(string line)
     {
         var parts = new List<string>();
         var sb = new StringBuilder();
         bool inQuotes = false;
         foreach (var c in line)
         {
             if (c == '"') { inQuotes = !inQuotes; continue; }
             if (char.IsWhiteSpace(c) && !inQuotes)
             {
                 if (sb.Length > 0) { parts.Add(sb.ToString()); sb.Clear(); }
                 continue;
             }
             sb.Append(c);
         }
         if (sb.Length > 0) parts.Add(sb.ToString());
         return parts.ToArray();
     }
 }
