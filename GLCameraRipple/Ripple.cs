using System;
using System.Drawing;
using System.Runtime.InteropServices;
using MonoTouch.CoreFoundation;

namespace GLCameraRipple
{
	public class RippleModel
	{
		Size screenSize;
		int poolHeight, poolWidth;
		int touchRadius, meshFactor;
	
	    float texCoordFactorS;
	    float texCoordOffsetS;
	    float texCoordFactorT;
	    float texCoordOffsetT;
    
	    // ripple coefficients
	    float [,] rippleCoeff;
	    
	    // ripple simulation buffers
	    float[,] rippleSource;
	    float[,] rippleDest;
	    
	    // data passed to GL
	    unsafe float *rippleVertices;
	    unsafe float *rippleTexCoords;
	    unsafe ushort *rippleIndicies;    
		
		public RippleModel (Size screenSize, int meshFactor, int touchRadius, Size textureSize)
		{
			this.screenSize = screenSize;
			this.meshFactor = meshFactor;
			this.touchRadius = touchRadius;
			poolWidth = screenSize.Width/meshFactor;
			poolHeight = screenSize.Height/meshFactor;
        
	        if ((float)screenSize.Height/screenSize.Width < (float)textureSize.Width/textureSize.Height){
	            texCoordFactorS = (float)(textureSize.Height*screenSize.Height)/(screenSize.Width*textureSize.Width);            
	            texCoordOffsetS = (1 - texCoordFactorS)/2f;
	            
	            texCoordFactorT = 1;
	            texCoordOffsetT = 0;
	        } else {
	            texCoordFactorS = 1;
	            texCoordOffsetS = 0;            
            
	            texCoordFactorT = (float)(screenSize.Width*textureSize.Width)/(textureSize.Height*screenSize.Height);
	            texCoordOffsetT = (1 - texCoordFactorT)/2f;
	        }
        
	        rippleCoeff = new float [touchRadius*2+1, touchRadius*2+1];
	        
	        // +2 for padding the border
	        rippleSource = new float [poolWidth+2, poolHeight+2];
	        rippleDest = new float [poolWidth+2, poolHeight+2];
        
			unsafe {
				int poolsize2 = poolWidth*poolHeight*2;
		        rippleVertices = (float *)Marshal.AllocHGlobal (poolsize2 * sizeof(float));
		        rippleTexCoords = (float *)Marshal.AllocHGlobal(poolsize2 * sizeof(float));
		        rippleIndicies = (ushort *)Marshal.AllocHGlobal((poolHeight-1)*(poolWidth*2+2)*sizeof(ushort));
			}
			
			InitRippleMap ();
        	InitRippleCoef ();
			InitMesh ();
	    }
		
		void InitRippleMap ()
		{
			Array.Clear (rippleSource, 0, (poolWidth+2) * (poolHeight+2));
			Array.Clear (rippleDest, 0, (poolWidth+2) * (poolHeight+2));
		}

		void InitRippleCoef ()
		{
		    for (int y = 0; y <= 2*touchRadius; y++){
		        for (int x = 0; x <= 2*touchRadius; x++){
		            float distance = (float)Math.Sqrt((x-touchRadius)*(x-touchRadius)+(y-touchRadius)*(y-touchRadius));
		            
		            if (distance <= touchRadius){
		                float factor = (distance/touchRadius);
		
		                // goes from -512 -> 0
		                rippleCoeff[y+1,x] = -((float)Math.Cos(factor*Math.PI)+1f) * 256f;
		            } else 
		                rippleCoeff[y+1,x] = 0;   
		        }
		    }    
		}

		unsafe void InitMesh ()
		{
		    for (int i = 0; i < poolHeight; i++){
		        for (int j = 0; j < poolWidth; j++){
		            rippleVertices[(i*poolWidth+j)*2+0] = -1f + j*(2f/(poolWidth-1));
		            rippleVertices[(i*poolWidth+j)*2+1] = 1f - i*(2f/(poolHeight-1));
		
		            rippleTexCoords[(i*poolWidth+j)*2+0] = (float)i/(poolHeight-1) * texCoordFactorS + texCoordOffsetS;
		            rippleTexCoords[(i*poolWidth+j)*2+1] = (1f - (float)j/(poolWidth-1)) * texCoordFactorT + texCoordFactorT;
		        }            
		    }
    
			uint index = 0;
		    for (int i=0; i<poolHeight-1; i++){
		        for (int j=0; j<poolWidth; j++){
		            if (i%2 == 0){
		                // emit extra index to create degenerate triangle
		                if (j == 0){
		                    rippleIndicies[index] = (ushort)(i*poolWidth+j);
		                    index++;                    
		                }
		                
		                rippleIndicies[index] = (ushort)(i*poolWidth+j);
		                index++;
		                rippleIndicies[index] = (ushort)((i+1)*poolWidth+j);
		                index++;
		                
		                // emit extra index to create degenerate triangle
		                if (j == (poolWidth-1)){
		                    rippleIndicies[index] = (ushort)((i+1)*poolWidth+j);
		                    index++;                    
		                }
		            } else {
		                // emit extra index to create degenerate triangle
		                if (j == 0){
		                    rippleIndicies[index] = (ushort)((i+1)*poolWidth+j);
		                    index++;
		                }
		                
		                rippleIndicies[index] = (ushort)((i+1)*poolWidth+j);
		                index++;
		                rippleIndicies[index] = (ushort)(i*poolWidth+j);
		                index++;
		                
		                // emit extra index to create degenerate triangle
		                if (j == (poolWidth-1)){
		                    rippleIndicies[index] = (ushort)(i*poolWidth+j);
		                    index++;
		                }
		            }
				}
			}
		}

		public unsafe float *Vertices {
			get { return rippleVertices; } 
		}
		
		public unsafe float *TexCoords {
			get { return rippleTexCoords; }
		}

		public unsafe ushort * Indices {
			get { return rippleIndicies; }
		}

		public int VertexSize {
			get { return poolWidth*poolHeight*2*sizeof(float); }
		}

		public int IndexSize {
			get { return (poolHeight-1)*(poolWidth*2+2)*sizeof(ushort); }
		}

		public int IndexCount { 
			get { return IndexSize/sizeof(ushort); }
		}

		public unsafe void RunSimulation ()
		{
			for (int y = 0; y < poolHeight; y++){
		        for (int x=0; x<poolWidth; x++)
		        {
		            // * - denotes current pixel
		            //
		            //       a 
		            //     c * d
		            //       b 
		            
		            // +1 to both x/y values because the border is padded
		            float a = rippleSource[y,x+1];
		            float b = rippleSource[y+2, x+1];
		            float c = rippleSource[y+1, x];
		            float d = rippleSource[y+1, x+2];
		            
		            float result = (a + b + c + d)/2f - rippleDest[y+1, x+1];
		            
		            result -= result/32f;
		            
		            rippleDest[y+1, + x+1] = result;
		        }            
		    }
		    
			for (int y = 0; y < poolHeight; y++){
		        for (int x=0; x<poolWidth; x++)
		        {
		            // * - denotes current pixel
		            //
		            //       a
		            //     c * d
		            //       b
		            
		            // +1 to both x/y values because the border is padded
		            float a = rippleDest[y, x+1];
		            float b = rippleDest[y+2, x+1];
		            float c = rippleDest[y+1, x];
		            float d = rippleDest[y+1, x+2];
		            
		            float s_offset = ((b - a) / 2048f);
		            float t_offset = ((c - d) / 2048f);
		            
		            // clamp
		            s_offset = (s_offset < -0.5f) ? -0.5f : s_offset;
		            t_offset = (t_offset < -0.5f) ? -0.5f : t_offset;
		            s_offset = (s_offset > 0.5f) ? 0.5f : s_offset;
		            t_offset = (t_offset > 0.5f) ? 0.5f : t_offset;
		            
		            float s_tc = (float)y/(poolHeight-1) * texCoordFactorS + texCoordOffsetS;
		            float t_tc = (1f - (float)x/(poolWidth-1)) * texCoordFactorT + texCoordOffsetT;
		            
		            rippleTexCoords[(y*poolWidth+x)*2+0] = s_tc + s_offset;
		            rippleTexCoords[(y*poolWidth+x)*2+1] = t_tc + t_offset;
		        }
		    }
		    
			var tmp = rippleDest;
			rippleSource = rippleDest;
			rippleSource = tmp;
		}

		void InitiateRippleAtLocation (PointF location)
		{
		    int xIndex = (int)((location.X / screenSize.Width) * poolWidth);
		    int yIndex = (int) ((location.X / screenSize.Height) * poolHeight);
    
		    for (int y=(int)yIndex-(int)touchRadius; y<=(int)yIndex+(int)touchRadius; y++)
		        for (int x=(int)xIndex-(int)touchRadius; x<=(int)xIndex+(int)touchRadius; x++){
		            if (x>=0 && x<poolWidth && y>=0 && y<poolHeight)
		                // +1 to both x/y values because the border is padded
        		        rippleSource[y+1,x+1] += rippleCoeff[(y-(yIndex-touchRadius)),x-(xIndex-touchRadius)];   
	            }
        }
    }    
}
