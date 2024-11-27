document.addEventListener('DOMContentLoaded', () => {
    const imageUrls = [
        '/images/arch1.png',
        '/images/arch2.png',
        '/images/arch3.png',
        '/images/arch4.png',
        '/images/arch5.png',
        '/images/arch6.png',
        '/images/arch7.png',
        '/images/arch8.png',
        '/images/arch9.png',
        '/images/arch10.png',
        '/images/arch11.png',
        '/images/arch12.png',
        '/images/arch13.png',
        '/images/arch14.png',
        '/images/arch15.png',
    ];

    const minThreshold = 30;
    const maxThreshold = 240;
    const thresholdIncrement = 30;
    let threshold = 90;
    const maxVisible = 10;
    const images = [];
    let currentIndex = 0;
    let lastMoveX = 0;
    let lastMoveY = 0;
    let initialMove = 250;
    let moved = false;
    let isImageClicked = false;

    const container = document.getElementById('dynamic-images');
    container.style.pointerEvents = 'auto';
    container.style.width = '100vw';
    container.style.height = 'calc(100vh - 40px)';
    container.style.position = 'absolute';
    container.style.top = '0';
    container.style.left = '0';

    const updateCounter = () => {
        const counter = document.getElementById('image-counter');
        counter.textContent = `${(currentIndex % imageUrls.length) + 1}/${imageUrls.length}`;
    };

    const createImageElement = (url) => {
        const img = document.createElement('img');
        img.src = url;
        img.style.position = 'absolute';
        img.style.transition = 'opacity 0.01s, width 0.01s, height 0.01s';
        img.style.opacity = '0';
        img.style.zIndex = '900';
        img.style.objectFit = 'cover';
        img.onload = () => {
            img.style.left = `${lastMoveX - 150}px`;
            img.style.top = `${lastMoveY - 150}px`;
            img.style.opacity = '1';
        };
        container.appendChild(img);
        return img;
    };

    const updateImages = (x, y) => {
        if (images.length >= maxVisible) {
            const oldestImage = images.shift();
            oldestImage.style.opacity = '0';
            setTimeout(() => container.removeChild(oldestImage), 2000);
        }

        const img = createImageElement(imageUrls[currentIndex]);
        img.addEventListener('click', handleImageClick);
        images.push(img);

        currentIndex = (currentIndex + 1) % imageUrls.length;
        updateCounter();
    };

    const updateThresholdDisplay = () => {
        const thresholdCounter = document.getElementById('threshold-counter');
        thresholdCounter.textContent = threshold.toString().padStart(3, '0');
    };

    const handleMouseMove = (event) => {
        if (!isImageClicked) {
            const x = event.clientX;
            const y = event.clientY;

            if (!moved) {
                if (Math.abs(x - lastMoveX) >= initialMove || Math.abs(y - lastMoveY) >= initialMove) {
                    moved = true;
                    lastMoveX = x;
                    lastMoveY = y;
                }
            } else {
                if (Math.abs(x - lastMoveX) >= threshold || Math.abs(y - lastMoveY) >= threshold) {
                    lastMoveX = x;
                    lastMoveY = y;
                    updateImages(x, y);
                }
            }
        }
    };

    const handleImageClick = (event) => {
        const clickedImage = event.target;
        enlargeImage(clickedImage);
    };

    const enlargeImage = (img) => {
        if (isImageClicked) return; // Prevent enlarging another image while one is already enlarged
    
        isImageClicked = true;

        // Prevent scrolling on the body but allow it inside the .middle-rectangle
        document.body.style.overflowY = 'hidden'; // Only block vertical scroll on the body
        const middleRectangle = document.querySelector('.middle-rectangle');
        if (middleRectangle) {
        middleRectangle.style.overflowY = 'auto'; // Ensure middle-rectangle is scrollable
        }

        // Fade out other images
        images.forEach(image => {
            if (image !== img) {
                image.style.transition = 'opacity 1.0s ease';
                image.style.opacity = '0'; // Fade out other images
            }
        });

        // Reveal the right container when an image is clicked
        const rightContainer = document.querySelector('.right');
        rightContainer.style.transition = 'opacity 1.0s ease';
        rightContainer.style.opacity = '1';
        
        // Create a wrapper div to overlay the image and center it
        const wrapper = document.createElement('div');
        wrapper.classList.add('wrapper'); // Add a class for consistent styling
        document.body.appendChild(wrapper); // Add wrapper to body
        wrapper.appendChild(img); // Move the image to the wrapper
    
        // Adjust height to consider bottom bar (40px)
        const bottomBarHeight = 40; // Height of the bottom bar
    
        // Get the current position and size of the image
        const rect = img.getBoundingClientRect();
        const imgWidth = rect.width;
        const imgHeight = rect.height;
    
        // Calculate the center position of the viewport
        const viewportWidth = window.innerWidth;
        const viewportHeight = window.innerHeight;
    
        // Calculate target position to center the image
        const targetX = (viewportWidth / 2) - (imgWidth / 2);
        const targetY = (viewportHeight / 2) - (imgHeight / 2) - (bottomBarHeight / 2);
    
        // Move the image to the center, keeping the same size
        img.style.transition = 'transform 2s ease'; // Smooth transition for sliding to center
        img.style.position = 'absolute';
        img.style.zIndex = '1000'; // Ensure it stays on top
        img.style.transformOrigin = 'center center'; // Ensure the transformation happens relative to the image's center
    
        // Move the image to the center without scaling
        img.style.transform = `translate(${targetX - rect.left}px, ${targetY - rect.top}px)`;
    
        // Listen for the end of the first transition (sliding to center)
        img.addEventListener('transitionend', function onMoveComplete() {
            // Remove this event listener to avoid triggering on future transitions
            img.removeEventListener('transitionend', onMoveComplete);
    
            // Now that the image is centered, enlarge it
            const scaleY = (viewportHeight - bottomBarHeight) / imgHeight; // Scale to match 100vh minus the bottom bar
    
            // Set up the scaling transition
            img.style.transition = 'transform 1.0s ease'; // Smooth transition for enlarging
            img.style.transform = `translate(${targetX - rect.left}px, ${targetY - rect.top}px) scale(${scaleY})`; // Scale uniformly
        });
    
        // Add a click event to close the image and restore its previous state
        img.addEventListener('click', () => {
            // Fade the right container out
            rightContainer.style.opacity = '0';
            rightContainer.style.pointerEvents = 'none';

            // Keep only the clicked image and center it
            images.forEach(image => {
                if (image !== img) {
                    container.removeChild(image); // Remove all other images
                }
            });

            // Restore the image size and position while keeping it centered
            img.style.transition = 'transform 1.0s ease';
            img.style.transform = `scale(0.5)`; // Reset scaling but keep it centered
            img.style.left = (viewportWidth / 2) - (imgWidth / 2);
            img.style.top = (viewportHeight / 2) - (imgHeight / 2);

            // Once minimized, keep it centered
            img.addEventListener('transitionend', function onMinimizeComplete() {
                img.removeEventListener('transitionend', onMinimizeComplete);
            
                // Calculate the viewport center
                const viewportWidth = window.innerWidth;
                const viewportHeight = window.innerHeight;
            
                // Get the image's current dimensions
                const imgWidth = img.getBoundingClientRect().width;
                const imgHeight = img.getBoundingClientRect().height;
            
                // Calculate the new left and top values to center the image
                const newLeft = (viewportWidth / 2) - (imgWidth / 2);
                const newTop = (viewportHeight / 2) - (imgHeight / 2);
            
                // Ensure the image stays centered after minimizing
                img.style.left = (viewportWidth / 2) - (imgWidth / 2);
                img.style.top = (viewportHeight / 2) - (imgHeight / 2);
                img.style.transform = `scale(1)`; // Keep image centered after minimizing
            
                document.body.removeChild(wrapper); // Remove the wrapper
                container.appendChild(img); // Move image back to container
            
                // Reset properties
                img.style.width = '';
                img.style.height = '';
                img.style.position = 'absolute'; // Keep absolute positioning to stay centered
                img.style.zIndex = '900'; // Ensure it stays on top
            
                isImageClicked = false;
                document.addEventListener('mousemove', handleMouseMove);
                document.body.style.overflow = 'auto';
            });
            
        });
    
        document.removeEventListener('mousemove', handleMouseMove); // Disable mouse move when enlarged
        document.body.style.overflow = 'hidden'; // Prevent body scroll when image is enlarged
    };   
    
    document.addEventListener('mousemove', handleMouseMove);
    updateCounter();
    updateThresholdDisplay();

    document.getElementById('decrease-threshold').addEventListener('click', () => {
        if (threshold > minThreshold) {
            threshold -= thresholdIncrement;
            updateThresholdDisplay();
        }
    });

    document.getElementById('increase-threshold').addEventListener('click', () => {
        if (threshold < maxThreshold) {
            threshold += thresholdIncrement;
            updateThresholdDisplay();
        }
    });
});
