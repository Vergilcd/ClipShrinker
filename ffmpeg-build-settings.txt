# Parts of ffmpeg that Clip Shrinker uses (everything else is switched off).
BSFS="h264_mp4toannexb hevc_mp4toannexb aac_adtstoasc extract_extradata null vp9_superframe vp9_superframe_split av1_frame_split av1_frame_merge h264_metadata hevc_metadata mpeg4_unpack_bframes noise setts"
FILTERS="acrossfade adelay aevalsrc afade aformat alimiter amix anoisesrc anull anullsrc apad aresample asetpts asetrate asplit atempo atrim blend color colorchannelmixer colorspace concat crop fade field format fps framerate highpass hue hwdownload lowpass lutyuv mix noise normalize null nullsink nullsrc overlay pad reverse rgbashift scale select sendcmd setpts setsar settb split thumbnail tonemap trim volume xfade zscale amerge pan channelmap channelsplit aloop areverse loop scale2ref"
DECODERS="h264 hevc av1 libdav1d vp9 vp8 mpeg4 mpeg2video mpeg1video msmpeg4v3 mjpeg png rawvideo prores aac aac_latm mp3 mp3float mp2 opus vorbis flac ac3 eac3 alac pcm_s16le pcm_s16be pcm_s24le pcm_s32le pcm_f32le pcm_u8 pcm_alaw pcm_mulaw wmav2 wmv3 vc1 gif bmp mjpegb"
ENCODERS="h264_nvenc hevc_nvenc av1_nvenc aac mjpeg png pcm_s16le rawvideo"
DEMUXERS="mov matroska avi mpegts mpegps flv asf ogg wav mp3 aac flac image2 image2pipe rawvideo m4v h264 hevc ivf gif concat"
MUXERS="mp4 mov wav pcm_s16le pcm_s16be pcm_f32le pcm_u8 image2 image2pipe rawvideo null mpjpeg"
PARSERS="h264 hevc av1 vp9 vp8 mpeg4video mpegvideo mpegaudio aac aac_latm opus vorbis flac ac3 mjpeg png"
PROTOCOLS="file pipe"
HWACCELS="h264_nvdec hevc_nvdec av1_nvdec vp9_nvdec vp8_nvdec mpeg4_nvdec mpeg2_nvdec mjpeg_nvdec vc1_nvdec"
join() { local p=$1; shift; for x in "$@"; do printf -- "--enable-%s=%s " "$p" "$x"; done; }
CONF="--disable-everything --disable-autodetect --disable-doc --disable-debug --disable-network --disable-avdevice \
 --enable-ffmpeg --enable-ffprobe --enable-swscale --enable-swresample --enable-avfilter --enable-version3 \
 --enable-ffnvcodec --enable-nvenc --enable-nvdec --enable-libzimg --enable-zlib --enable-libdav1d \
 $(join filter $FILTERS) $(join decoder $DECODERS) $(join encoder $ENCODERS) $(join demuxer $DEMUXERS) \
 $(join muxer $MUXERS) $(join parser $PARSERS) $(join protocol $PROTOCOLS) $(join hwaccel $HWACCELS) $(join bsf $BSFS)"
