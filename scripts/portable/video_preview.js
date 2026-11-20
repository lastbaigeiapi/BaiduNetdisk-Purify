'use strict';

// Fall back to extensions accepted by the online player when the file has no category.
const VIDEO_EXTENSION = /\.(?:wmv|rmvb|mpeg4|mpeg2|flv|avi|3gp|mpga|ts|qt|rm|wmz|wmd|wvx|wmx|wm|mpg|mp4|mkv|mpeg|mov|asf|m4v|m3u8|webm|vob|mts|dat|m3u|mtv|amv|m2ts)$/i;

function videoFileCondition(file, path) {
  return `(1===Number(${file}.category)||(null==${file}.category&&${VIDEO_EXTENSION}.test(${path})))`;
}

module.exports = { videoFileCondition };
