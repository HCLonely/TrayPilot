// Internal XAML rendering only: no desktop/window capture or screen coordinates.
// Shared buffers are deliberately small and never hold Explorer object pointers.
struct IconImageRequest {
    bool busy{};
    ULONGLONG attempted{};
};

void PublishIconImage(IconPixels& slot, int width, int height, HRESULT error, BYTE const* pixels) noexcept {
    InterlockedIncrement(&slot.sequence);
    slot.width=width;
    slot.height=height;
    slot.error=error;
    slot.timestamp=GetTickCount64();
    if(pixels && width>0 && height>0) memcpy(slot.pixels,pixels,width*height*4);
    InterlockedIncrement(&slot.sequence);
}

// Own all arguments across suspension; Context can disappear while XAML completes.
// A stalled operation occupies only its one bounded request, without blocking Tick.
winrt::fire_and_forget RenderIconImage(std::shared_ptr<Session> session,
    std::shared_ptr<IconImageRequest> request, FrameworkElement element, int index) {
    HRESULT error=S_OK;
    try {
        if(session->stopped() || !session->data->appearanceRequested) {
            request->busy=false;
            co_return;
        }
        double width=element.ActualWidth(), height=element.ActualHeight();
        if(element.Visibility()==Visibility::Collapsed || !element.XamlRoot() ||
            !std::isfinite(width) || !std::isfinite(height) || width<=0 || height<=0)
            throw hresult_error(E_BOUNDS);
        double scale=std::min(IconMaxWidth/width,IconMaxHeight/height);
        int targetWidth=std::max(1,static_cast<int>(width*scale));
        int targetHeight=std::max(1,static_cast<int>(height*scale));
        Media::Imaging::RenderTargetBitmap bitmap;
        co_await bitmap.RenderAsync(element,targetWidth,targetHeight);
        auto buffer=co_await bitmap.GetPixelsAsync();
        int w=bitmap.PixelWidth(), h=bitmap.PixelHeight();
        if(w<=0 || h<=0 || w>IconMaxWidth || h>IconMaxHeight || buffer.Length()!=w*h*4)
            throw hresult_error(E_BOUNDS);
        // Remove transparent layout padding, keeping all actual glyph layers/colors.
        int left=w, top=h, right=-1, bottom=-1;
        for(int y=0;y<h;y++) for(int x=0;x<w;x++) if(buffer.data()[(y*w+x)*4+3]) {
            left=std::min(left,x); top=std::min(top,y);
            right=std::max(right,x); bottom=std::max(bottom,y);
        }
        if(right<left || bottom<top) throw hresult_error(E_FAIL);
        int croppedWidth=right-left+1, croppedHeight=bottom-top+1;
        std::vector<BYTE> pixels(croppedWidth*croppedHeight*4);
        for(int y=0;y<croppedHeight;y++)
            memcpy(pixels.data()+y*croppedWidth*4,buffer.data()+((top+y)*w+left)*4,croppedWidth*4);
        // Do not show a stale rendering if visibility changed during the await.
        if(!session->stopped() && session->data->appearanceRequested &&
            element.XamlRoot() && element.Visibility()!=Visibility::Collapsed)
            PublishIconImage(session->data->images[index],croppedWidth,croppedHeight,S_OK,pixels.data());
        else error=E_BOUNDS;
    } catch(...) {
        error=to_hresult();
    }
    if(FAILED(error) && !session->stopped())
        PublishIconImage(session->data->images[index],0,0,error,nullptr);
    request->busy=false;
}
