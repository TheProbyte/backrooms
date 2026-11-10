"""Joins the captured frames and the soundtrack into the finished tape: a VHS finish (colour bleed, noise, a soft
picture) and the encodes. Drafts: --phone only (small MP4 to review). Final cut: the full MP4, and --webm for Unity.
python build.py <framesDir> <soundtrack.wav> <outBase> [--phone] [--webm]"""
import subprocess, sys
import imageio_ffmpeg

frames, wav, out = sys.argv[1], sys.argv[2], sys.argv[3]
ff = imageio_ffmpeg.get_ffmpeg_exe()
vhs = ('chromashift=cbh=3:crh=-3:crv=1,'
       'noise=c0s=4:c0f=t+u:c1s=3:c1f=t:c2s=3:c2f=t,'
       'gblur=sigma=0.7,unsharp=5:5:0.35,'
       'eq=saturation=0.93:contrast=1.03:gamma=1.02,'
       'format=yuv420p')
base = [ff, '-y', '-loglevel', 'error', '-framerate', '24', '-i', f'{frames}/f%05d.png', '-i', wav, '-vf', vhs, '-shortest']
if '--phone' in sys.argv:
    subprocess.run(base + ['-c:v', 'libx264', '-preset', 'medium', '-crf', '24', '-c:a', 'aac', '-b:a', '128k',
                           '-movflags', '+faststart', out + '_phone.mp4'], check=True)
    print('wrote', out + '_phone.mp4')
else:
    subprocess.run(base + ['-c:v', 'libx264', '-preset', 'slow', '-crf', '18', '-c:a', 'aac', '-b:a', '192k',
                           '-movflags', '+faststart', out + '.mp4'], check=True)
    print('wrote', out + '.mp4')
if '--webm' in sys.argv:
    subprocess.run(base + ['-c:v', 'libvpx', '-b:v', '5M', '-crf', '8', '-c:a', 'libvorbis', '-q:a', '5', out + '.webm'], check=True)
    print('wrote', out + '.webm')
